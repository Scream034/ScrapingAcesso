namespace ScraperAcesso.Product;

using ScraperAcesso.Components;

public class WebProduct(in string url) : BaseProduct(string.Empty, url, BaseProduct.DefaultPrice, BaseProduct.DefaultCount)
{
    public virtual async Task ParseAsync(ChromiumScraper browser)
    {
        await Task.CompletedTask;
    }
}