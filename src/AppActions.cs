namespace ScraperAcesso;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Product.Parsers.Stem;
using ScraperAcesso.Product.Stem;
using ScraperAcesso.Sites.Authorization;

/// <summary>
/// Handles application actions such as authorization and product parsing.
/// Provides methods for user interaction and manages settings.
/// </summary>
public sealed class AppActions(ChromiumScraper scraper, SettingsManager settingsManager)
{
    private readonly ChromiumScraper _scraper = scraper;
    private readonly SettingsManager _settingsManager = settingsManager;

    public async Task PerformAuthorizationAsync()
    {
        Log.Print("--- Authorization ---");

        // Get user input for authorization
        var url = GetUserInput("Input URL", _settingsManager.GetDecryptedAuthUrl());
        var username = GetUserInput("Input Username", _settingsManager.GetDecryptedUsername());
        var password = GetUserInput("Input Password", isPassword: true);

        if (string.IsNullOrWhiteSpace(password))
        {
            // if user did not provide a password, try to use the last saved one
            Log.Print("No password provided. Trying to use last saved password.");
            password = _settingsManager.GetDecryptedPassword();
            if (!string.IsNullOrEmpty(password))
            {
                Log.Print($"Using last saved password.");
            }
        }

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log.Error("URL, username, and password cannot be empty. Authorization canceled.");
            return;
        }

        var authInfo = new AuthorizationInfo(username, password);
        using var authorization = new AuthorizationService(url, authInfo);

        var success = await authorization.ParseAsync(_scraper);

        if (success)
        {
            Log.Print("Authorization successful.");
            // Сохраняем новые данные после успешной авторизации
            _settingsManager.UpdateAndSaveAuthInfo(url, username, password);
        }
        else
        {
            // Спрашиваем сохранить ли неудачные данные
            Log.Error("Authorization failed. Do you want to save the entered credentials? (y/n)");
            var saveResponse = Console.ReadLine()?.Trim().ToLower();
            if (saveResponse == "y" || saveResponse == "yes")
            {
                _settingsManager.UpdateAndSaveAuthInfo(url, username, password);
                Log.Print("Credentials saved for future use.");
            }
            else
            {
                Log.Print("Credentials not saved.");
            }

            Log.Error("Authorization failed.");
        }

        await authorization.CloseAsync();
    }

    public async Task ParseStemProductsAsync()
    {
        Log.Print("Input catalog URL (or press Enter to cancel):");
        string? url = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Print("URL not specified. Operation canceled.");
            return;
        }

        Log.Print("--- Start parsing Stem products ---");
        var stemCatalogParser = new StemCatalogParser(url, productUrl => new WebStemProduct(productUrl));
        var products = await stemCatalogParser.ParseAsync(_scraper);

        if (products.Count == 0)
        {
            Log.Error("No products found.");
        }
        else
        {
            Log.Print($"Successfully processed {products.Count} products.");
        }
        Log.Print("--- Stem products parsing completed ---");
    }

    /// <summary>
    /// Prompts the user for input with an optional default value.
    /// If the user presses Enter without input, returns the default value.
    /// </summary>
    private string GetUserInput(string prompt, string? defaultValue = null, bool isPassword = false)
    {
        if (!string.IsNullOrWhiteSpace(defaultValue) && !isPassword)
        {
            Log.Print($"{prompt} (default: '{defaultValue}'):");
        }
        else
        {
            Log.Print($"{prompt}:");
        }

        if (isPassword)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        // if the input is null or whitespace, return the default value
        string? input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue ?? string.Empty : input;
    }
}