namespace ScraperAcesso.Sites.Editor;

using Microsoft.Playwright;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public sealed class EditorService(string url) : BaseSiteParser
{
    public static class XPath
    {
        public const string AddProductButton = "//button[@title='Добавить товар']";
    }

    public override string URL { get; } = url;
    public override IPage? Page { get; protected set; }

    public override async Task<bool> ParseAsync(ChromiumScraper browser)
    {
        Page = await browser.NewPageAsync(Constants.Contexts.Editor);
        return true;
    }

    public static ILocator GetAddProductButton(IPage page)
    {
        return page.Locator(XPath.AddProductButton).First;
    }

    public static bool HasAddProductButton(IPage page)
    {
        return page.Locator(XPath.AddProductButton).First != null;
    }
}