namespace ScraperAcesso.Product.Parsers.Stem;

using GenerativeAI;
using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product.Stem;

public sealed class StemCatalogParser(Uri url, Func<Uri, WebStemProduct> productFactory) : BaseCatalogParser<WebStemProduct>(url, productFactory)
{
    public static class XPath
    {
        public const string ToProductLink = "//div[@data-btn-config]/a";
        public const string CurrentPage = "//div[@class='module-pagination__wrapper']/span";
        public const string NextPageButton = "//a[@title='След.']";
    }

    public IPage? Page { get; private set; }

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
        while (await NextPageAsync())
        {
            int currentPage = await GetCurrentPageAsync();
            Log.Print($"Parsing page {currentPage}...");

            Log.Print("Collecting product links...");
            var productLinks = await Page.QuerySelectorAllAsync(XPath.ToProductLink);
            if (productLinks == null || !productLinks.Any())
            {
                Log.Error("No product links found.");
                break;
            }

            Log.Print($"Found {productLinks.Count} product links on page {currentPage}.");
            for (int i = 0; i < productLinks.Count; i++)
            {
                var productLink = productLinks[i];
                var href = await productLink.GetAttributeAsync("href");
                if (string.IsNullOrWhiteSpace(href))
                {
                    Log.Warning($"Found a product link with no href attribute at - Index: {i}, Current Page: {currentPage}");
                    continue;
                }

                var productUrl = new Uri(URL, href);
                var product = await ParseProductAsync(scraper, productUrl);
                if (product != null)
                {
                    products.Add(product);
                    await product.CloseAsync();
                }
            }
        }

        return products;
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

        var currentPageElement = await Page.QuerySelectorAsync(XPath.CurrentPage);
        if (currentPageElement == null)
        {
            Log.Warning("Current page element not found.");
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
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        return true;
    }
}