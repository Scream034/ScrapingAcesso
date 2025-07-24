using ScraperAcesso.Components;

namespace ScraperAcesso.Product.Parsers;

public abstract class BaseCatalogParser<ProductType> where ProductType : WebProduct
{
    public string URL { get; private set; }
    
    // Храним "инструкцию" по созданию продукта
    private readonly Func<string, ProductType> _productFactory;

    // Конструктор теперь принимает URL и фабрику
    public BaseCatalogParser(string url, Func<string, ProductType> productFactory)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }
        URL = url;
        _productFactory = productFactory ?? throw new ArgumentNullException(nameof(productFactory));
    }

    public abstract Task<ICollection<ProductType>> ParseAsync(ChromiumScraper browser);

    protected virtual async Task<ProductType?> ParseProductAsync(ChromiumScraper browser, string url)
    {
        // Используем фабрику для создания экземпляра
        ProductType product = _productFactory(url);
        await product.ParseAsync(browser);
        return product;
    }
}