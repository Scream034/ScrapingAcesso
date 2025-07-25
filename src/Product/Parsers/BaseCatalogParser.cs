namespace ScraperAcesso.Product.Parsers;

using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public abstract class BaseCatalogParser<ProductType> where ProductType : WebProduct
{
    public Uri URL { get; protected set; }
    public IPage? Page { get; protected set; }

    protected readonly Func<Uri, ProductType> _productFactory;
    protected readonly int? _maxProductsToParse;

    // Конструктор теперь принимает URL и фабрику
    public BaseCatalogParser(Uri url, Func<Uri, ProductType> productFactory, int? maxProductsToParse = null)
    {
        if (string.IsNullOrWhiteSpace(url.ToString()))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }
        URL = url;
        _productFactory = productFactory ?? throw new ArgumentNullException(nameof(productFactory));
        _maxProductsToParse = maxProductsToParse;
    }

    public abstract Task<ICollection<ProductType>> ParseAsync(ChromiumScraper browser);

    public async Task CloseAsync()
    {
        if (Page != null)
        {
            await Page.CloseAsync();
            Page = null;
            Log.Print($"Closed catalog page: {URL}");
        }
    }

    protected virtual async Task<ProductType?> ParseProductAsync(ChromiumScraper browser, Uri url)
    {
        // Используем фабрику для создания экземпляра
        ProductType product = _productFactory(url);
        await product.ParseAsync(browser);
        return product;
    }

}