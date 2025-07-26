namespace ScraperAcesso.Product.Parsers.Stem;

using System.Diagnostics;
using Microsoft.Playwright;
using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product.Stem;
using ScraperAcesso.Utils;

public sealed class StemCatalogParser(Uri url, Func<Uri, WebStemProduct> productFactory, int? _maxProductsToParse = null) : BaseCatalogParser<WebStemProduct>(url, productFactory, _maxProductsToParse)
{
    public static class XPath
    {
        public const string ToProductLink = "//div[@data-btn-config]/a";
        public const string CurrentPage = "//div[@class='module-pagination__wrapper']/span[@class='cur module-pagination__item']";
        public const string NextPageButton = "//div[contains(@class, 'arrows-pagination')]//a[@title='След.' and last()]";
    }

    public override async Task<ICollection<WebStemProduct>> ParseAsync(ChromiumScraper scraper)
    {
        Page = await scraper.NewPageAsync(Constants.Contexts.CatalogParser);
        if (Page == null)
        {
            Log.Error("Failed to create a new page in the browser.");
            return [];
        }

        Page = await ChromiumScraper.OpenWithRetriesAsync(Page, URL.ToString(), waitUntil: WaitUntilState.DOMContentLoaded);
        if (Page == null)
        {
            Log.Error($"Failed to open the URL: {URL}");
            return [];
        }

        var products = new List<WebStemProduct>();
        bool morePages;
        do
        {
            int currentPage = await GetCurrentPageAsync();
            Log.Print($"Parsing page {currentPage}...");

            Log.Print("Collecting product links...");
            var productLinks = await Page.Locator(XPath.ToProductLink).AllAsync();
            if (productLinks.Count == 0)
            {
                Log.Warning("No product links found on this page. Finishing parsing.");
                break;
            }

            Log.Print($"Found {productLinks.Count} product links on page {currentPage}.");
            foreach (var productLink in productLinks)
            {
                if (_maxProductsToParse.HasValue && products.Count >= _maxProductsToParse.Value)
                {
                    Log.Print($"Reached the specified limit of {_maxProductsToParse.Value} products. Stopping parser.");
                    goto EndParsing; // Выходим из обоих циклов
                }

                var href = await productLink.GetAttributeAsync("href");
                if (string.IsNullOrWhiteSpace(href))
                {
                    Log.Warning($"Found a product link with no href attribute on page {currentPage}.");
                    continue;
                }

                var productUrl = new Uri(URL, href);
                var parseResult = await ParseProductAsync(scraper, productUrl);
                if (parseResult.IsParsed)
                {
                    products.Add(parseResult.Product);
                }
                else
                {
                    Log.Warning($"Failed to parse product on page {currentPage} with url {productUrl} ({parseResult.Product.TranslitedTitle}).");
                }

                await parseResult.Product.CloseAsync();
            }

            // Проверяем наличие следующей страницы и переходим
            morePages = await NextPageAsync();

        } while (morePages);

    EndParsing: // Метка для выхода из циклов
        await WaitForBackgroundTasksAsync();
        await CloseAsync();
        return products;
    }

    /// <summary>
    /// Новый приватный метод для ожидания завершения всех фоновых задач (ИИ и изображения).
    /// </summary>
    private static async Task WaitForBackgroundTasksAsync()
    {
        // Получаем задачи ожидания от наших сервисов
        var imageTask = QueuedImageDownloader.WaitForIdleAsync();
        var aiTask = GeminiBatchProcessor.WaitForIdleAsync();

        // Если обе задачи уже завершены, выходим сразу
        if (imageTask.IsCompleted && aiTask.IsCompleted)
        {
            Log.Print("All background tasks were already completed.");
            return;
        }

        Log.Print("Catalog parsing complete. Waiting for background tasks to finish...");
        var stopwatch = Stopwatch.StartNew();

        // Асинхронно ждем, пока обе задачи не будут завершены
        while (!imageTask.IsCompleted || !aiTask.IsCompleted)
        {
            string imageStatus = imageTask.IsCompleted ? "Done" : "Working...";
            string aiStatus = aiTask.IsCompleted ? "Done" : "Working...";
            Log.Print($"  -> Waiting... [Images: {imageStatus}] [AI SEO: {aiStatus}]");
            await Task.Delay(2000); // Проверяем статус каждые 2 секунды
        }

        // Финальное ожидание на случай, если одна из задач завершилась прямо перед проверкой
        await Task.WhenAll(imageTask, aiTask);

        stopwatch.Stop();
        Log.Print($"All background tasks completed successfully in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    }

    /// <summary>
    /// Gets the current page number from the pagination element.
    /// </summary>
    /// <returns>The current page number, or 0 if it could not be determined.</returns>
    public async Task<int> GetCurrentPageAsync()
    {
        if (Page == null)
        {
            Log.Warning("GetCurrentPage called before page is initialized.");
            return 0;
        }

        IElementHandle? currentPageElement;
        try
        {
            currentPageElement = await Page.WaitForSelectorAsync(XPath.CurrentPage);
            if (currentPageElement == null)
            {
                Log.Warning("Current page element not found.");
                return 0;
            }
        }
        catch (TimeoutException ex)
        {
            Log.Warning($"Current page element not found within the timeout period: {ex.Message}");
            return 0;
        }

        var pageText = await currentPageElement.InnerTextAsync();
        if (int.TryParse(pageText, out var currentPage))
        {
            return currentPage;
        }

        Log.Warning($"Failed to parse current page number from text: {pageText}");
        return 0;
    }

    /// <summary>
    /// Navigates to the next page of products.
    /// </summary>
    /// <returns>true if the next page was successfully navigated to, false otherwise.</returns>
    public async Task<bool> NextPageAsync()
    {
        if (Page == null)
        {
            Log.Warning("NextPage called before page is initialized.");
            return false;
        }

        var nextButton = await Page.QuerySelectorAsync(XPath.NextPageButton);
        if (nextButton == null)
        {
            Log.Warning("Next page button not found.");
            return false;
        }

        await nextButton.ClickAsync();
        await Page.WaitForLoadStateAsync();
        return true;
    }
}