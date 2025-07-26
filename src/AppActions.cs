namespace ScraperAcesso;

using System.Diagnostics;
using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Product;
using ScraperAcesso.Product.Parsers.Stem;
using ScraperAcesso.Product.Stem;
using ScraperAcesso.Sites.Authorization;
using ScraperAcesso.Sites.Editor;

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
        var stemCatalogParser = new StemCatalogParser(new(url), productUrl => new WebStemProduct(productUrl, _settingsManager));

        if (_settingsManager.GetAutoSeoEnabled())
        {
            var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
            if (!GeminiService.Initialize(apiKey))
            {
                Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
                _settingsManager.SetAutoSeoEnabled(false);
                return;
            }
            GeminiBatchProcessor.Initialize();
        }

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
    /// Toggles the automatic SEO generation setting.
    /// </summary>
    public Task ToggleAutoSeoGeneration()
    {
        var currentState = _settingsManager.GetAutoSeoEnabled();
        var newState = !currentState;
        _settingsManager.SetAutoSeoEnabled(newState);
        Log.Print($"Automatic SEO generation on product parse is now {(newState ? "ENABLED" : "DISABLED")}.");

        // check for gemeni api
        if (newState)
        {
            var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
            if (string.IsNullOrWhiteSpace(apiKey) || !GeminiService.Initialize(apiKey))
            {
                Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
                _settingsManager.SetAutoSeoEnabled(false);
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    public async Task GenerateSeoForAllRawProductsAsync()
    {
        Log.Print("--- Start SEO Generation for Raw Products ---");

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

    public async Task RunEditorModeAsync()
    {
        Log.Print("--- Starting Editor Mode ---");
        var stopwatch = Stopwatch.StartNew();

        // --- Фаза 1: Сбор данных от пользователя и подготовка ---
        Log.Print("--- [Phase 1: Preparation] ---");

        var authUrl = _settingsManager.GetDecryptedAuthUrl();
        var editorUrl = GetUserInput("Enter the editor URL", authUrl);
        if (string.IsNullOrWhiteSpace(editorUrl))
        {
            Log.Error("Editor URL cannot be empty. Operation canceled.");
            return;
        }

        var countInput = GetUserInput("How many products to add? (Press Enter for all)", "all");
        bool processAll = countInput.Equals("all", StringComparison.OrdinalIgnoreCase);
        int? productCount = processAll ? null : (int.TryParse(countInput, out int c) ? c : null);

        if (!processAll && (productCount == null || productCount <= 0))
        {
            Log.Error("Invalid number of products. Please enter a positive integer or 'all'.");
            return;
        }

        // Загружаем товары, которые еще не были добавлены
        var productsToProcess = await BaseProduct.LoadProductsByStatusAsync(false);
        if (productsToProcess.Count == 0)
        {
            Log.Print("No products with 'Not Added' status found. Nothing to do.");
            return;
        }

        // Отбираем нужное количество, если указано
        var productsToUpload = productCount.HasValue
            ? [.. productsToProcess.Take(productCount.Value)]
            : productsToProcess;

        Log.Print($"Found {productsToProcess.Count} products to add. Processing {productsToUpload.Count} of them.");

        // --- Фаза 2: Автоматическая авторизация ---
        Log.Print("--- [Phase 2: Automatic Authorization] ---");
        var username = _settingsManager.GetDecryptedUsername();
        var password = _settingsManager.GetDecryptedPassword();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log.Error("Authorization credentials are not set in settings. Please run 'Authorize' from the main menu first.");
            return;
        }

        var authService = new AuthorizationService(authUrl, new AuthorizationInfo(username, password));
        if (!await authService.ParseAsync(_scraper))
        {
            Log.Error("Automatic authorization failed. Please check your saved credentials.");
            await authService.CloseAsync();
            return;
        }
        Log.Print("Authorization successful.");

        await authService.CloseAsync();

        // --- Фаза 3: Запуск обработки в редакторе ---
        Log.Print("--- [Phase 3: Processing in Editor] ---");
        var editorService = new EditorService(editorUrl);

        var editorSuccess = await editorService.ProcessProductsAsync(productsToUpload, _scraper);
        if (editorSuccess)
        {
            Log.Print("All products were successfully processed in the editor.");
        }
        else
        {
            Log.Error("Some products failed to be processed in the editor.");
        }

        await editorService.CloseAsync();

        stopwatch.Stop();
        Log.Print($"--- Editor Mode Finished. Total time: {stopwatch.Elapsed.TotalMinutes:F2} minutes. ---");
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

        GeminiBatchProcessor.Initialize();

        // 2. Создаем тестовый продукт с заранее заданными данными
        var testProduct = new BaseProduct(
            title: "Игровой ноутбук CyberForce X-15",
            url: new("http://example.com/product/cyberforce-x15")
        )
        {
            Description = "Мощный игровой ноутбук CyberForce X-15 оснащен процессором Intel Core i9 последнего поколения и видеокартой NVIDIA GeForce RTX 4080. 15.6-дюймовый QHD экран с частотой обновления 240 Гц обеспечивает плавное и четкое изображение. Система охлаждения с двумя вентиляторами и испарительной камерой предотвращает перегрев даже при максимальных нагрузках. Клавиатура с RGB-подсветкой. Объем оперативной памяти 32 ГБ DDR5.",
            Price = 185000,
            Attributes =
            [
                new("Процессор", "Intel Core i9-13900HX"),
                new("Видеокарта", "NVIDIA GeForce RTX 4080 Laptop"),
                new("Экран", "15.6\" QHD (2560x1440) 240Hz"),
                new("Память", "32 ГБ DDR5")
            ]
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
    /// Starts a test for the AI batch generation of SEO content.
    /// </summary>
    public async Task TestAiBatchGenerationAsync()
    {
        Log.Print("--- Starting AI Batch Generation Test ---");

        // 1. Проверяем, что API ключ настроен
        var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || !GeminiService.Initialize(apiKey))
        {
            Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
            return;
        }

        GeminiBatchProcessor.Initialize();

        // 2. Создаем несколько тестовых продуктов
        var testProducts = new List<BaseProduct>
        {
            new(
                title: "Игровой ноутбук CyberForce X-15",
                url: new Uri("http://example.com/product/cyberforce-x15")
            ) {
                Description = "Мощный игровой ноутбук CyberForce X-15 оснащен процессором Intel Core i9 последнего поколения и видеокартой NVIDIA GeForce RTX 4080. 15.6-дюймовый QHD экран с частотой обновления 240 Гц обеспечивает плавное и четкое изображение. Система охлаждения с двумя вентиляторами.",
                Price = 185000
            },
            new(
                title: "Эргономичное кресло 'Aetheria'",
                url: new Uri("http://example.com/product/aetheria-chair")
            ) {
                Description = "Офисное кресло Aetheria с поддержкой поясницы и синхромеханизмом качания. Обивка из дышащей сетчатой ткани, подлокотники регулируются в четырех направлениях. Идеально для долгой работы за компьютером, снижает нагрузку на позвоночник.",
                Price = 25000
            },
            new(
                title: "Умная колонка 'Aura'",
                url: new Uri("http://example.com/product/aura-speaker")
            ) {
                Description = "Беспроводная умная колонка Aura с голосовым ассистентом Алиса. Качественный объемный звук на 360 градусов. Подключается по Wi-Fi и Bluetooth. Управляет устройствами умного дома, включает музыку, рассказывает новости и погоду.",
                Price = 8990
            }
        };

        Log.Print($"Created {testProducts.Count} mock products for the batch test.");

        // 3. Ставим все продукты в очередь на обработку
        Log.Print("Enqueuing products for batch processing...");
        foreach (var product in testProducts)
        {
            GeminiBatchProcessor.Enqueue(product);
        }

        // 4. Ждем, пока Batch Processor выполнит свою работу.
        // Время ожидания должно быть больше, чем интервал проверки очереди (dispatchInterval)
        const int waitSeconds = 15;
        Log.Print($"Waiting for {waitSeconds} seconds for the batch to be processed by the background task...");
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds));

        // 5. Проверяем результаты
        Log.Print("--- Verification ---");
        int successCount = 0;
        foreach (var product in testProducts)
        {
            Console.WriteLine("--------------------");
            Log.Print($"Checking product: '{product.Title}'");
            if (product.SEO != null)
            {
                Log.Print("[SUCCESS] SEO data was generated.");
                Log.Print($"  -> Short Desc: {product.ShortDescription?[..Math.Min(product.ShortDescription.Length, 70)]}...");
                Log.Print($"  -> SEO Sentence: {product.SEO.SeoSentence}");
                Log.Print($"  -> Keywords: {product.SEO.Keywords}");
                successCount++;
            }
            else
            {
                Log.Error("[FAILURE] SEO data is missing for this product.");
            }
        }
        Console.WriteLine("--------------------");

        // 6. Выводим итоговый результат теста
        if (successCount == testProducts.Count)
        {
            Log.Print($"--- AI Batch Generation Test Successful. Processed {successCount}/{testProducts.Count} products. ---");
        }
        else
        {
            Log.Error($"--- AI Batch Generation Test Partially Failed. Processed {successCount}/{testProducts.Count} products. ---");
        }
    }

    /// <summary>
    /// Запускает полный сквозной (E2E) тест: Парсинг каталога -> Обработка в редакторе.
    /// </summary>
    public async Task RunFullCycleTestAsync()
    {
        Log.Print("--- Starting Full End-to-End Cycle Test ---");

        // --- Вводные данные от пользователя ---
        Console.WriteLine("Enter the catalog URL to start parsing from (default: https://stemco.ru/catalog/steam_konstruirovanie/):");
        string? catalogUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(catalogUrl))
        {
            catalogUrl = "https://stemco.ru/catalog/steam_konstruirovanie/";
        }

        Console.WriteLine("How many products to test with? (default: 2)");
        string? countInput = Console.ReadLine();
        if (!int.TryParse(countInput, out int productCount) || productCount <= 0)
        {
            productCount = 2;
        }

        var apiKey = _settingsManager.GetDecryptedGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || !GeminiService.Initialize(apiKey))
        {
            Log.Error("Gemini API key is not configured or invalid. Please set it up first via the main menu.");
            return;
        }

        GeminiBatchProcessor.Initialize();

        // --- Фаза 1: Парсинг и обработка ---
        Log.Print($"--- [Phase 1: Parsing] Starting to parse {productCount} products from catalog... ---");
        var stopwatch = Stopwatch.StartNew();

        var stemCatalogParser = new StemCatalogParser(new(catalogUrl), productUrl => new WebStemProduct(productUrl, _settingsManager), productCount);
        var parsedProducts = await stemCatalogParser.ParseAsync(_scraper);

        stopwatch.Stop();
        Log.Print($"--- [Phase 1: Parsing] Finished in {stopwatch.Elapsed.TotalSeconds:F2} seconds. ---");

        if (parsedProducts.Count == 0)
        {
            Log.Error("No products were parsed from the catalog. Cannot continue the test.");
            return;
        }

        // --- Фаза 2: Промежуточная верификация ---
        Log.Print($"--- [Phase 2: Verification] Verifying saved products... ---");
        var productsForEditor = await BaseProduct.LoadProductsByStatusAsync(false);
        if (productsForEditor.Count == 0)
        {
            Log.Error("Verification failed: No products with 'Not Added' status found on disk.");
            return;
        }
        Log.Print($"Found {productsForEditor.Count} products ready for the editor.");

        Console.WriteLine("\nParsing complete. Press Enter to start adding these products to the editor...");
        Console.ReadLine();

        // --- Фаза 3: Работа редактора ---
        Log.Print("--- [Phase 3: Editor] Starting to process products in the editor... ---");
        stopwatch.Restart();

        Log.Print("Enter the editor URL to start processing from:");
        string? editorUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(editorUrl))
        {
            editorUrl = _settingsManager.GetDecryptedAuthUrl();
            Log.Warning($"Dont set editor url, use auth url: {editorUrl}");
        }

        var editorService = new EditorService(editorUrl);
        bool editorSuccess = await editorService.ParseAsync(_scraper);

        stopwatch.Stop();
        Log.Print($"--- [Phase 3: Editor] Finished in {stopwatch.Elapsed.TotalSeconds:F2} seconds. ---");

        // --- Фаза 4: Финальная верификация ---
        if (editorSuccess)
        {
            Log.Print("--- [Phase 4: Final Verification] ---");
            var addedProducts = await BaseProduct.LoadProductsByStatusAsync(true);
            if (addedProducts.Count >= productsForEditor.Count)
            {
                Log.Print($"[SUCCESS] Test completed successfully! {productsForEditor.Count} products were processed and marked as added.");
            }
            else
            {
                Log.Error($"[PARTIAL FAILURE] Test finished, but verification failed. Expected at least {productsForEditor.Count} added products, but found {addedProducts.Count}.");
            }
        }
        else
        {
            Log.Error("[FAILURE] The editor service reported a failure. Test did not complete successfully.");
        }

        await editorService.CloseAsync();
    }

    /// <summary>
    /// Запускает тест добавления одного товара с предварительной автоматической авторизацией.
    /// </summary>
    public async Task RunSingleProductEditorTestAsync()
    {
        Log.Print("--- Starting Single Product Addition Test (with Auto-Login) ---");

        // --- Фаза 1: Подготовка данных ---
        Log.Print("--- [Phase 1: Preparation] Loading one 'Not Added' product... ---");
        var productsForEditor = await BaseProduct.LoadProductsByStatusAsync(false);
        if (productsForEditor.Count == 0)
        {
            Log.Error("No products with 'Not Added' status found on disk. Cannot run the test.");
            return;
        }

        const string productTranslitedTitleToAdd = "centr-konstruirovaniya";
        var productToAdd = productsForEditor.Where(x => x.TranslitedTitle == productTranslitedTitleToAdd).FirstOrDefault();
        if (productToAdd == null)
        {
            Log.Error($"Product with translited title '{productTranslitedTitleToAdd}' not found in the list of products with 'Not Added' status. Cannot run the test.");
            return;
        }

        Log.Print($"Selected product for test: '{productToAdd.Title}'");

        // --- Фаза 2: Авторизация ---
        Log.Print("--- [Phase 2: Authorization] Attempting to log in... ---");
        var authUrl = _settingsManager.GetDecryptedAuthUrl();
        var username = _settingsManager.GetDecryptedUsername();
        var password = _settingsManager.GetDecryptedPassword();

        if (string.IsNullOrWhiteSpace(authUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log.Error("Authorization credentials are not set in settings. Please configure them first.");
            return;
        }

        var authInfo = new AuthorizationInfo(username, password);
        var authService = new AuthorizationService(authUrl, authInfo);

        // Выполняем авторизацию. Она оставит нас на готовой к работе странице редактора.
        bool authSuccess = await authService.ParseAsync(_scraper);

        if (!authSuccess)
        {
            Log.Error("Authorization failed. Cannot continue the test.");
            return;
        }
        Log.Print("Authorization successful. Proceeding to add product.");

        await authService.CloseAsync();

        Log.Print($"--> Input the editor URL to start processing from (default '{authUrl}'):");
        string? editorUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(editorUrl))
        {
            editorUrl = authUrl;
            Log.Warning($"Dont set editor url, use auth url: {editorUrl}");
        }

        // --- Фаза 3: Добавление товара ---
        Log.Print("--- [Phase 3: Editor] Starting to process the single product... ---");
        var stopwatch = Stopwatch.StartNew();

        var editorService = new EditorService(editorUrl);

        // Вызываем наш новый гибкий метод, передавая ему коллекцию из ОДНОГО товара
        bool editorSuccess = await editorService.ProcessProductsAsync([productToAdd], _scraper);

        stopwatch.Stop();
        Log.Print($"--- [Phase 3: Editor] Finished in {stopwatch.Elapsed.TotalSeconds:F2} seconds. ---");

        // --- Фаза 4: Финальная верификация ---
        Log.Print("--- [Phase 4: Final Verification] ---");
        if (editorSuccess)
        {
            // Проверяем прямо по объекту, был ли он помечен как добавленный
            if (productToAdd.IsAdded)
            {
                Log.Print($"[SUCCESS] Test completed successfully! Product '{productToAdd.Title}' was processed and marked as added.");
            }
            else
            {
                Log.Error("[FAILURE] Editor service reported success, but the product was not marked as added on disk.");
            }
        }
        else
        {
            Log.Error("[FAILURE] The editor service reported a failure. Product was not added.");
        }

        await editorService.CloseAsync();
    }

    /// <summary>
    /// Prompts the user for input with an optional default value.
    /// If the user presses Enter without input, returns the default value.
    /// </summary>
    private static string GetUserInput(string prompt, string? defaultValue = null, bool isPassword = false)
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