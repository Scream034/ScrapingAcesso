namespace ScraperAcesso.Sites.Authorization;

using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Sites.Editor;

public sealed class AuthorizationService : BaseSiteParser
{
    public static class XPath
    {
        public const string LoginInput = "//input[@name='username']";
        public const string PasswordInput = "//input[@name='password']";
        public const string SubmitButton = "//button[@type='submit']";
    }

    public override string URL { get; }
    public AuthorizationInfo AuthInfo { get; }
    public override IPage? Page { get; protected set; }

    public AuthorizationService(in string url, in AuthorizationInfo authInfo)
    {
        URL = url;
        AuthInfo = authInfo;
    }

    public override async Task<bool> ParseAsync(ChromiumScraper browser)
    {
        Page = await browser.NewPageAsync(Constants.Contexts.Editor);
        var response = await Page.GotoAsync(URL, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        if (response == null || !response.Ok)
        {
            Log.Error($"Failed to load authorization page: {URL}");
            return false;
        }
        else if (EditorService.HasAddProductButton(Page))
        {
            Log.Print("Already authorized, editor page loaded successfully.");
            return true;
        }

        try
        {
            Log.Print("Waiting for authorization elements to load...");
            await Page.Locator(XPath.LoginInput).WaitForAsync(new() { Timeout = 10000 });

            Log.Print($"Filling in authorization form for {AuthInfo.Username}...");
            await Page.FillAsync(XPath.LoginInput, AuthInfo.Username);

            Log.Print($"Filling in password for {AuthInfo.Username}...");
            await Page.FillAsync(XPath.PasswordInput, AuthInfo.Password);

            Log.Print($"Submitting authorization form for {AuthInfo.Username}...");
            await Page.ClickAsync(XPath.SubmitButton);

            Log.Print("Waiting for page to load after submission...");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            return true;
        }
        catch (TimeoutException)
        {
            Log.Error("Timeout while waiting for authorization elements to load.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred during authorization: {ex.Message}");
            return false;
        }
    }
}