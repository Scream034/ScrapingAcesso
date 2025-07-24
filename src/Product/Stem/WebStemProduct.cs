namespace ScraperAcesso.Product.Stem;

using System.Threading.Tasks;

using Microsoft.Playwright;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Utils;

public sealed class WebStemProduct(in Uri url) : WebProduct(url)
{
    public static class XPath
    {
        public const string Title = "//div/h1[contains(@class, 'js-popup-title')]";
        public const string Description = "//div[@id='desc']/div[@itemprop='description']";
        public const string Price = "//meta[@itemprop='price']";
        public const string Images = "//img[contains(@class, 'detail-gallery-big__picture')]";
        public const string AttributeName = "//div[contains(@class, 'properties')]//div[contains(@class, 'js-prop-title')]";
        public const string AttributeValue = "//div[contains(@class, 'properties')]//div[contains(@class, 'js-prop-value')]";
    }

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

        var descriptionTask = GetDescriptionAsync(Page);
        var priceTask = GetPriceAsync(Page);
        var attributesTask = GetAttributesAsync(Page);
        var imageUrlsTask = GetImageUrlsAndSaveAsync(Page, ImageFolderPath);

        Description = await descriptionTask;
        Price = await priceTask;
        Attributes = await attributesTask;
        AllImages = await imageUrlsTask;

        if (AllImages == null || AllImages.Count == 0)
        {
            Log.Warning($"No images found for product: {URL}");
            AllImages = [];
        }

        Log.Print($"Parsed product: {Title} - {Price} - {Description}");
        if (string.IsNullOrEmpty(Title) || Price <= 0)
        {
            Log.Error($"Failed to parse product details: {URL}");
            return false;
        }
        else if (Title.Length > MaxTitleLength)
        {
            Log.Error($"Product title exceeds maximum length ({MaxTitleLength}): {Title}");
            return false;
        }
        else if (ShortDescription?.Length > MaxShortDescriptionLength)
        {
            Log.Error($"Product short description exceeds maximum length ({MaxShortDescriptionLength}): {ShortDescription}");
            return false;
        }

        // Save product details to the database, no need to wait for the result
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

        return names.Zip(values, (name, value) => new ProductAttribute(name, value)).ToList();
    }

    ///<summary>
    /// Get all image URLs from the product page.
    /// </summary>
    public static async Task<List<string>?> GetImageUrlsAsync(IPage page)
    {
        if (page == null)
        {
            Log.Error("Page is not initialized. Cannot download images.");
            return null;
        }

        var elements = await page.Locator(XPath.Images).AllAsync();
        if (elements == null || !elements.Any())
        {
            Log.Warning("No images found on the product page.");
            return null;
        }

        var imageUrls = new List<string>();
        var baseUrl = new Uri(page.Url);
        foreach (var element in elements)
        {
            var src = await element.GetAttributeAsync("data-src");
            if (!string.IsNullOrEmpty(src))
            {
                imageUrls.Add(new Uri(baseUrl, src).ToString());
            }
        }

        return imageUrls;
    }

    /// <summary>
    /// Get image URLs and download them to the specified folder.
    /// </summary>
    public static async Task<List<string>?> GetImageUrlsAndSaveAsync(IPage page, string destinationFolder)
    {
        var imageUrls = await GetImageUrlsAsync(page);
        if (imageUrls == null || imageUrls.Count == 0)
        {
            Log.Warning("No image URLs found to download.");
            return null;
        }

        // Ensure the destination folder exists
        Directory.CreateDirectory(destinationFolder);

        // Download and save images
        var savedFilePaths = await ImageDownloader.DownloadAndSaveAsync(imageUrls, destinationFolder);
        return savedFilePaths;
    }
}