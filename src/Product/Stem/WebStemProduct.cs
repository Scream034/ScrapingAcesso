namespace ScraperAcesso.Product.Stem;

using System.Threading.Tasks;

using Microsoft.Playwright;
using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Utils;
using ScraperAcesso.Sites.Editor;

public sealed class WebStemProduct(in Uri url, SettingsManager settingsManager) : WebProduct(url)
{
    private readonly SettingsManager _settingsManager = settingsManager;

    public static class XPath
    {
        public const string Title = "//div/h1[contains(@class, 'js-popup-title')]";
        public const string Description = "//div[@id='desc']/div[@itemprop='description']";
        public const string Price = "//meta[@itemprop='price']";
        public const string Images = "//img[contains(@class, 'detail-gallery-big__picture')]";
        public const string AttributeName = "//div[contains(@class, 'properties')]//div[contains(@class, 'js-prop-title')]";
        public const string AttributeValue = "//div[contains(@class, 'properties')]//div[contains(@class, 'js-prop-value')]";
    }

    private static readonly HttpClient _httpClient = new();

    public override async Task<bool> ParseAsync(ChromiumScraper scraper)
    {
        Page = await scraper.NewPageAsync(Constants.Contexts.ProductParser);
        if (Page == null)
        {
            Log.Error("Failed to create a new page for product parsing.");
            return false;
        }

        Page = await ChromiumScraper.OpenWithRetriesAsync(Page, URL.ToString(), 3, WaitUntilState.Load, 30_000);
        if (Page == null)
        {
            Log.Error($"Failed to open product page: {URL}");
            return false;
        }

        Title = await GetTitleAsync(Page);

        if (string.IsNullOrWhiteSpace(Title))
        {
            Log.Error($"Failed to parse product title, it is empty. URL: {URL}");
            return false;
        }
        else if (HasProductIO(TranslitedTitle))
        {
            Log.Warning($"Product with title '{Title}' already exists in IO. Skipping.");
            return false;
        }

        var descriptionTask = GetDescriptionAsync(Page);
        var priceTask = GetPriceAsync(Page);
        var attributesTask = GetAttributesAsync(Page);
        // Этот метод теперь будет фильтровать изображения по размеру ДО скачивания
        var imageUrlsTask = EnqueueImagesForDownloadAsync(Page, ImageFolderPath);

        Description = await descriptionTask;
        Price = await priceTask;
        Attributes = await attributesTask;
        AllImages = await imageUrlsTask;

        if (AllImages == null || AllImages.Count == 0)
        {
            Log.Warning($"No images found for product: {URL}");
            AllImages = [];
        }

        Log.Print($"Parsed product: {Title}");
        if (Title.Length > MaxTitleLength)
        {
            Log.Error($"Product title exceeds maximum length ({MaxTitleLength}): {Title}");
            return false;
        }

        if (_settingsManager.GetAutoSeoEnabled() && !string.IsNullOrWhiteSpace(Description))
        {
            Log.Print($"Auto-SEO is enabled. Generating content for '{URL}'...");
            GeminiBatchProcessor.Enqueue(this);
        }

        _ = SaveAsync();

        return true;
    }

    /// <summary>
    /// Get the product title from the page.
    /// </summary>
    public static async Task<string> GetTitleAsync(IPage page)
    {
        return await page.Locator(XPath.Title).InnerTextAsync();
    }

    /// <summary>
    /// Get the product description from the page.
    /// </summary>
    public static async Task<string> GetDescriptionAsync(IPage page)
    {
        return await page.Locator(XPath.Description).InnerTextAsync();
    }

    /// <summary>
    /// Get the product price from the page.
    /// </summary>
    public static async Task<int> GetPriceAsync(IPage page)
    {
        var priceString = await page.Locator(XPath.Price).GetAttributeAsync("content") ?? string.Empty;
        return int.TryParse(priceString, out var price) ? price : 0;
    }

    /// <summary>
    /// Get all product attributes from the product page.
    /// </summary>
    public static async Task<List<ProductAttribute>> GetAttributesAsync(IPage page)
    {
        var names = await page.Locator(XPath.AttributeName).AllInnerTextsAsync();
        var values = await page.Locator(XPath.AttributeValue).AllInnerTextsAsync();

        return [.. names.Zip(values, (name, value) => new ProductAttribute(name, value))];
    }

    /// <summary>
    /// Gets all image URLs from the product page.
    /// </summary>
    public static async Task<List<string>> GetImageUrlsAsync(IPage page)
    {
        var imageUrls = new List<string>();
        if (page == null) return imageUrls;

        var elements = await page.QuerySelectorAllAsync(XPath.Images);
        if (elements.Count == 0) return imageUrls;

        var baseUrl = new Uri(page.Url);
        for (var i = 0; i < MaxImagesCount; i++)
        {
            var element = elements.ElementAtOrDefault(i);
            if (element == null) break;

            var src = await element.GetAttributeAsync("data-src");
            if (!string.IsNullOrEmpty(src))
            {
                imageUrls.Add(new Uri(baseUrl, src).ToString());
            }
        }

        return imageUrls;
    }

    /// <summary>
    /// Finds image URLs on the page and enqueues them for background download.
    /// </summary>
    /// <returns>A list of predicted local file paths for the enqueued images.</returns>
    /// <summary>
    public static async Task<List<string>> EnqueueImagesForDownloadAsync(IPage page, string destinationFolder)
    {
        var imageUrls = await GetImageUrlsAsync(page);
        if (imageUrls == null || imageUrls.Count == 0)
        {
            Log.Warning("No image URLs found to enqueue.");
            return [];
        }

        Directory.CreateDirectory(destinationFolder);

        var enqueuedFilePaths = new List<string>();
        foreach (var url in imageUrls)
        {
            try
            {
                // Отправляем HEAD-запрос, чтобы получить только заголовки, а не весь файл
                using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(url));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                response.EnsureSuccessStatusCode(); // Убеждаемся, что запрос прошел успешно (код 2xx)

                // Проверяем заголовок Content-Length
                if (response.Content.Headers.ContentLength is long contentLength && contentLength > EditorService.MaxImageSizeInBytes)
                {
                    // Если размер файла превышает лимит, логируем и пропускаем его
                    Log.Warning($"Image '{Path.GetFileName(url)}' is too large ({contentLength / 1024:N0} KB). Skipping download.");
                    continue; // Переходим к следующему URL
                }

                // Если размер в норме (или неизвестен), ставим в очередь на скачивание
                var predictedPath = QueuedImageDownloader.Enqueue(url, destinationFolder);
                enqueuedFilePaths.Add(predictedPath);
            }
            catch (Exception ex)
            {
                // Если не удалось проверить размер (например, сервер не отвечает или не отдает Content-Length),
                // мы на всякий случай все равно ставим его в очередь.
                // Альтернатива - пропустить, но так надежнее.
                Log.Warning($"Could not pre-check size for image '{Path.GetFileName(url)}'. Enqueuing anyway. Reason: {ex.Message}");
                var predictedPath = QueuedImageDownloader.Enqueue(url, destinationFolder);
                enqueuedFilePaths.Add(predictedPath);
            }
        }

        Log.Print($"Enqueued {enqueuedFilePaths.Count} images for download after size pre-check.");
        return enqueuedFilePaths;
    }
}