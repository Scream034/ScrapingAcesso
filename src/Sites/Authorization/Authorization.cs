namespace ScraperAcesso.Sites.Authorization;

using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public class Authorization : IBaseSiteParser
{
    public static class XPath
    {
        public const string LoginInput = "//input[@name='username']";
        public const string PasswordInput = "//input[@name='password']";
        public const string SubmitButton = "//button[@type='submit']";
    }

    public string URL { get; }
    public AuthorizationInfo AuthInfo { get; }
    public IPage? Page { get; private set; }

    public Authorization(in string url, in AuthorizationInfo authInfo)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }
        else if (authInfo == null)
        {
            throw new ArgumentNullException(nameof(authInfo), "Authorization info cannot be null.");
        }

        URL = url;
        AuthInfo = authInfo;
    }

    public async Task<bool> ParseAsync(ChromiumScraper browser)
    {
        Page = await browser.NewPageAsync(Constants.Contexts.CatalogParser);
        if (Page == null)
        {
            throw new InvalidOperationException("Failed to create a new page in the browser.");
        }

        Page = await ChromiumScraper.OpenWithRetriesAsync(Page, URL);
        if (Page == null)
        {
            throw new InvalidOperationException($"Failed to open the URL: {URL}");
        }

        Log.Print($"Wait visible for login input at {XPath.LoginInput}...");
        if (!await Page.IsVisibleAsync(XPath.LoginInput))
        {
            Log.Error($"Login input not found at {XPath.LoginInput}. Please check the URL or the page structure.");
            return false;
        }

        Log.Print($"Filling in authorization form for {AuthInfo.Username}...");
        await Page.FillAsync(XPath.LoginInput, AuthInfo.Username);

        Log.Print($"Filling in password for {AuthInfo.Username}...");
        await Page.FillAsync(XPath.PasswordInput, AuthInfo.Password);

        Log.Print("Submitting the authorization form...");
        await Page.ClickAsync(XPath.SubmitButton);

        return true;
    }

    public void Dispose()
    {
        Log.Print("Disposing Authorization resources...");
        Page?.CloseAsync().Wait();
        Page = null;

        GC.SuppressFinalize(this);
        Log.Print("Disposing Authorization resources completed.");
    }
}