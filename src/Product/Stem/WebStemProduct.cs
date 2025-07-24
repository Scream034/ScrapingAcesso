namespace ScraperAcesso.Product.Stem;

using ScraperAcesso.Components;

public class WebStemProduct(in string url) : WebProduct(url)
{
    public override async Task ParseAsync(ChromiumScraper browser)
    {
        // Создаю новый контекст для 
        await Task.CompletedTask;
    }
}