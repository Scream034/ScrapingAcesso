namespace ScraperAcesso.Product;

using Microsoft.Playwright;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public abstract class WebProduct(in Uri url) : BaseProduct(string.Empty, url, BaseProduct.DefaultPrice, BaseProduct.DefaultCount)
{
    public IPage? Page { get; protected set; }

    public abstract Task<bool> ParseAsync(ChromiumScraper browser);

    public virtual async Task CloseAsync()
    {
        if (Page != null)
        {
            await Page.CloseAsync();
            Page = null;
            Log.Print($"Product closed: {URL}");
        }
    }
}