namespace ScraperAcesso;

using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Product;
using ScraperAcesso.Product.Parsers.Stem;
using ScraperAcesso.Product.Stem;
using ScraperAcesso.Sites.Authorization;

/// <summary>
/// Handles application actions such as authorization and product parsing.
/// Provides methods for user interaction and manages settings.
/// </summary>
public sealed class AppActions(ChromiumScraper scraper, SettingsManager settingsManager)
{
    public const int GemeniAPIDelay = 1400;

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
        var stemCatalogParser = new StemCatalogParser(new(url), productUrl => new WebStemProduct(productUrl));
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

    public async Task GenerateSeoForAllProductsAsync()
    {
        Log.Print("--- Start SEO Generation for Products ---");

        var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || !GeminiService.Initialize(apiKey))
        {
            Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
            return;
        }

        var allProducts = await BaseProduct.LoadAllAsync();
        var productsWithoutSeo = allProducts.Where(p => p.SEO == null && !string.IsNullOrWhiteSpace(p.Description)).ToList();

        if (productsWithoutSeo.Count == 0)
        {
            Log.Print("All products already have SEO data. Nothing to do.");
            return;
        }

        Log.Print($"Found {productsWithoutSeo.Count} products without SEO. Starting generation...");
        int successCount = 0;

        foreach (var product in productsWithoutSeo)
        {
            Log.Print($"Generating SEO for: '{product.Title}'...");
            if (await GeminiService.GenerateContentAsync(product))
            {
                _ = product.SaveAsync();
                successCount++;
                Log.Print($"Successfully generated and saved SEO for '{product.Title}'.");
            }
            else
            {
                Log.Warning($"Skipped SEO generation for '{product.Title}' due to an error.");
            }
            await Task.Delay(GemeniAPIDelay); // wait for the API delay
        }

        Log.Print($"--- SEO Generation Finished. Successfully processed {successCount} of {productsWithoutSeo.Count} products. ---");
    }

    public Task ConfigureGeminiApiKey()
    {
        Log.Print("--- Configure Gemini API Key ---");
        var currentKey = _settingsManager.GetDecryptedGeminiApiKey();

        if (!string.IsNullOrEmpty(currentKey))
        {
            Log.Print($"Current key found: ...{currentKey.Substring(currentKey.Length - 4)}");
        }

        Log.Print("Please enter your new Google Gemini API key (or press Enter to cancel):");
        string? newKey = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(newKey))
        {
            Log.Print("Operation canceled. Key was not changed.");
            return Task.CompletedTask;
        }

        if (GeminiService.Initialize(newKey))
        {
            _settingsManager.UpdateAndSaveGeminiApiKey(newKey);
            Log.Print("Gemini API key successfully verified and saved.");
        }
        else
        {
            Log.Error("The provided API key is invalid or could not be verified. It was not saved.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Start the test for AI generation of SEO content.
    /// </summary>
    public async Task TestAiGenerationAsync()
    {
        Log.Print("--- Starting AI Generation Test ---");

        // 1. Проверяем, что API ключ настроен
        var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || !GeminiService.Initialize(apiKey))
        {
            Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
            return;
        }

        // 2. Создаем тестовый продукт с заранее заданными данными
        var testProduct = new BaseProduct(
            title: "Игровой ноутбук CyberForce X-15",
            url: new("http://example.com/product/cyberforce-x15")
        )
        {
            Description = "Мощный игровой ноутбук CyberForce X-15 оснащен процессором Intel Core i9 последнего поколения и видеокартой NVIDIA GeForce RTX 4080. 15.6-дюймовый QHD экран с частотой обновления 240 Гц обеспечивает плавное и четкое изображение. Система охлаждения с двумя вентиляторами и испарительной камерой предотвращает перегрев даже при максимальных нагрузках. Клавиатура с RGB-подсветкой. Объем оперативной памяти 32 ГБ DDR5.",
            Price = 185000,
            Attributes = new List<ProductAttribute>
            {
                new("Процессор", "Intel Core i9-13900HX"),
                new("Видеокарта", "NVIDIA GeForce RTX 4080 Laptop"),
                new("Экран", "15.6\" QHD (2560x1440) 240Hz"),
                new("Память", "32 ГБ DDR5")
            }
        };

        Log.Print($"Running test for mock product: '{testProduct.Title}'");

        // 4. Выводим результат
        if (await GeminiService.GenerateContentAsync(testProduct) && testProduct.SEO != null)
        {
            Log.Print("--- AI Generation Test Successful ---");
            Log.Print($"Generated Short Description: {testProduct.ShortDescription}");
            Log.Print($"Generated SEO Sentence: {testProduct.SEO.SeoSentence}");
            Log.Print($"Generated Keywords: {testProduct.SEO.Keywords}");
        }
        else
        {
            Log.Error("--- AI Generation Test Failed ---");
            Log.Warning("Check the logs above for details from the Gemini Service.");
        }
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