namespace ScraperAcesso.Sites.Authorization;

using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Sites.Editor;

public sealed class AuthorizationService(in string url, in AuthorizationInfo authInfo) : BaseSiteParser
{
    public static class XPath
    {
        public const string LoginInput = "//input[@name='username']";
        public const string PasswordInput = "//input[@name='password']";
        public const string SubmitButton = "//button[@type='submit']";
    }

    public override string URL { get; } = url;
    public AuthorizationInfo AuthInfo { get; } = authInfo;
    public override IPage? Page { get; protected set; }

    public override async Task<bool> ParseAsync(ChromiumScraper scraper)
    {
        Page = await scraper.NewPageAsync(Constants.Contexts.Editor);
        var response = await Page.GotoAsync(URL);

        if (response == null || !response.Ok)
        {
            Log.Error($"Failed to load authorization page: {URL}");
            return false;
        }
        else if (await Page.QuerySelectorAsync(EditorService.XPath.AddProductButton) != null)
        {
            Log.Print("Already authorized, editor page loaded successfully.");
            return true;
        }

        try
        {
            Log.Print("Waiting for authorization elements to load...");
            var loginInput = Page.Locator(XPath.LoginInput);
            if (!await loginInput.IsVisibleAsync())
            {
                Log.Print("Login input not found, try found enabled editor button...");
                var enableEditorButton = Page.Locator(EditorService.XPath.EnableEditorButton).First;

                try
                {
                    await enableEditorButton.WaitForAsync(new() { Timeout = 5000, State = WaitForSelectorState.Attached });

                    Log.Print("Clicking on enable editor button...");
                    await enableEditorButton.ClickAsync();

                    Log.Print("Waiting for editor page to load...");
                    await Page.Locator(EditorService.XPath.AddProductButton).WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Attached });
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error($"Enable editor button not found, authorization page not loaded: {ex.Message}");
                    return false;
                }
            }

            Log.Print($"Filling in authorization form for {AuthInfo.Username}...");
            await Page.FillAsync(XPath.LoginInput, AuthInfo.Username);

            Log.Print($"Filling in password for {AuthInfo.Username}...");
            await Page.FillAsync(XPath.PasswordInput, AuthInfo.Password);

            Log.Print($"Submitting authorization form for {AuthInfo.Username}...");
            await Page.ClickAsync(XPath.SubmitButton);

            Log.Print("Clicking on enable editor button...");
            await Page.Locator(EditorService.XPath.EnableEditorButton).First.ClickAsync();

            Log.Print("Waiting for editor page to load...");
            await Page.Locator(EditorService.XPath.AddProductButton).WaitForAsync(new() { Timeout = 10000, State = WaitForSelectorState.Attached });

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