namespace ScraperAcesso.Product.Parsers;

using ScraperAcesso.Components;

public abstract class BaseCatalogParser<ProductType> where ProductType : WebProduct
{
    public Uri URL { get; private set; }
    
    // Храним "инструкцию" по созданию продукта
    private readonly Func<Uri, ProductType> _productFactory;

    // Конструктор теперь принимает URL и фабрику
    public BaseCatalogParser(Uri url, Func<Uri, ProductType> productFactory)
    {
        if (string.IsNullOrWhiteSpace(url.ToString()))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }
        URL = url;
        _productFactory = productFactory ?? throw new ArgumentNullException(nameof(productFactory));
    }

    public abstract Task<ICollection<ProductType>> ParseAsync(ChromiumScraper browser);

    protected virtual async Task<ProductType?> ParseProductAsync(ChromiumScraper browser, Uri url)
    {
        // Используем фабрику для создания экземпляра
        ProductType product = _productFactory(url);
        await product.ParseAsync(browser);
        return product;
    }
}