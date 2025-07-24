namespace ScraperAcesso;

using Microsoft.Playwright;

using ScraperAcesso.Sites;
using ScraperAcesso.Sites.Authorization;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Menu;
using ScraperAcesso.Product.Parsers.Stem;
using ScraperAcesso.Product.Stem;

public static class Program
{
    public static ConsoleMenuManager MenuManager = new("Main Menu");

    public static void Main(string[] args)
    {
        Constants.EnsureDirectoriesExist();
        Log.Initialize();

        Log.Print($"Starting {Constants.AppName}...");

        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            Log.Warning("Ctrl+C pressed. Cleaning up resources before exit...");
            eventArgs.Cancel = true; // Отменяем принудительное завершение, чтобы дать Dispose отработать
            Log.Dispose();
            Environment.Exit(0); // Завершаем программу после очистки
        };

        HandleMain(args).Wait();
    }

    public static async Task HandleMain(string[] args)
    {
        Log.Print("Initializing Playwright...");

        // Инициализация Playwright и создание браузера
        await using var scraper = await ChromiumScraper.CreateAsync();
        var catalogContext = await scraper.CreateContextAsync(Constants.Contexts.CatalogParser);
        var productContext = await scraper.CreateContextAsync(Constants.Contexts.ProductParser);
        Log.Print($"Contexts created: {Constants.Contexts.CatalogParser}, {Constants.Contexts.ProductParser}");

        // Установливаем для контекста каталога только запросы типа "document"
        await catalogContext.RouteAsync("**/*", static route =>
        {
            if (route.Request.ResourceType != "document")
            {
                route.AbortAsync();
            }
            else
            {
                route.ContinueAsync();
            }
        });

        // Устанавливаем для контекста продукта только запросы типа "document" и "image"
        await productContext.RouteAsync("**/*", static route =>
        {
            if (route.Request.ResourceType != "document" && route.Request.ResourceType != "image")
            {
                route.AbortAsync();
            }
            else
            {
                route.ContinueAsync();
            }
        });

        Log.Print("Playwright initialized successfully.");

        // Инициализация менеджера меню
        await InitializeMenuAsync(scraper);
    }

    private static async Task InitializeMenuAsync(ChromiumScraper scraper)
    {
        Log.Print("Initializing menu...");

        CreateScraperMenu(scraper);

        MenuManager.AddAction("Authorize", async () => await authorizeAsync(scraper));
        MenuManager.SetExitOption("Exit");

        Log.Print("Menu initialized successfully.");

        // --- 4. Запуск цикла меню ---
        await MenuManager.RunAsync();

        Log.Print("Menu loop finished. Exiting application.");
    }

    private static ConsoleMenuManager CreateScraperMenu(ChromiumScraper scraper)
    {
        Log.Print("Creating Scraper Menu...");
        var scraperMenu = MenuManager.AddSubMenu("Scraper Menu");
        scraperMenu.AddAction("Parse Stem Products", async () => await parseStemProductsAsync(scraper));

        Log.Print("Scraper Menu created successfully.");
        return scraperMenu;
    }

    #region Actions

    private static async Task authorizeAsync(ChromiumScraper scraper)
    {
        Log.Print("Starting authorization...");

        Log.Print("Please enter the URL for authorization (or press Enter to use default):");
        string? url = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Print("Using default URL for authorization.");
            url = "https://accounts.nethouse.ru/auth/realms/nethouse/protocol/openid-connect/auth?client_id=nethouse-constructor&kc_idp_hint=&login_hint=&redirect_uri=https%3A%2F%2Fnethouse.ru%2Fnew-auth%2F%2Fhandle_redirect%2FeyJyZWRpcmVjdF91cmkiOiIvY29uc3RydWN0b3Ivc2lnbmluIiwiaW5faWZyYW1lIjpmYWxzZX0&response_type=code&scope=openid+profile+email&state=state";
        }

        Log.Print("Please enter your username:");
        string? username = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(username))
        {
            Log.Print("Using default username: comlevalyubov@yandex.ru");
            username = "comlevalyubov@yandex.ru";
        }

        Log.Print("Please enter your password:");
        string? password = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(password))
        {
            Log.Print("Using default password: NJ2139dhwds");
            password = "NJ2139dhwds";
        }

        Authorization authorization = new(url, new(username, password));
        Log.Print($"Authorization created with URL: {authorization.URL}");

        await authorization.ParseAsync(scraper);
        Log.Print("Authorization completed successfully.");

        // Закрытие страницы после завершения
        authorization.Dispose();
    }

    private static async Task parseStemProductsAsync(ChromiumScraper scraper)
    {
        Log.Print("Write catalog URL (or press Enter to exit):");
        string? url = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Print("No URL provided. Exiting...");
            return;
        }

        Log.Print("Starting Stem products parsing...");

        // Создаем экземпляр парсера с фабрикой для создания WebStemProduct
        var stemCatalogParser = new StemCatalogParser(
            url,
            url => new WebStemProduct(url)
        );

        Log.Print($"Parsing products from URL: {stemCatalogParser.URL}");

        var products = await stemCatalogParser.ParseAsync(scraper);
        if (products.Count == 0)
        {
            Log.Error("No products found during parsing.");
        }
        else
        {
            Log.Print($"Successfully parsed {products.Count} products.");
        }

        Log.Print("Stem products parsing completed.");
    }

    #endregion
}