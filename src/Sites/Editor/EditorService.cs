namespace ScraperAcesso.Sites.Editor;

using Microsoft.Playwright;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public sealed class EditorService : BaseSiteParser
{
    public static class XPath
    {
        public const string AddProductButton = "//button[@title='Добавить товар']";
    }

    public override string URL { get; }
    public override IPage? Page { get; protected set; }

    public EditorService(string url)
    {
        URL = url;
    }

    public override async Task<bool> ParseAsync(ChromiumScraper browser)
    {
        Page = await browser.NewPageAsync(Constants.Contexts.Editor);
        var response = await Page.GotoAsync(URL, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        if (response == null || !response.Ok)
        {
            Log.Error($"Failed to load editor page: {URL}");
            return false;
        }

        Log.Print("Editor page loaded successfully.");
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