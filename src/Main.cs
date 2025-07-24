namespace ScraperAcesso;

using ScraperAcesso.Sites.Authorization;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Menu;
using ScraperAcesso.Components.Settings;

public static class Program
{
    public static readonly ConsoleMenuManager MenuManager = new("Main Menu");

    public static async Task Main(string[] args)
    {
        Constants.EnsureDirectoriesExist();
        Log.Initialize();
        Log.Print($"Starting {Constants.AppName} v1.0...");

        // Настройка корректного завершения
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            Log.Warning("Ctrl+C pressed. Disposing resources...");
            eventArgs.Cancel = true;
            Log.Dispose();
            Environment.Exit(0);
        };
        
        await HandleMain(args);
    }

    public static async Task HandleMain(string[] args)
    {
        // 1. Инициализация ключевых сервисов
        Log.Print("Initializing services...");
        var settingsManager = new SettingsManager();
        settingsManager.Load();

        await using var scraper = await ChromiumScraper.CreateAsync();
        var appActions = new AppActions(scraper, settingsManager);
        
        // 2. Настройка контекстов
        await ConfigureBrowserContexts(scraper);

        // 3. Создание и запуск меню
        InitializeMenu(appActions);
        await MenuManager.RunAsync();

        Log.Print("Menu loop finished. Exiting application.");
    }
    
    private static void InitializeMenu(AppActions actions)
    {
        Log.Print("Initializing menu...");

        var scraperMenu = MenuManager.AddSubMenu("Scraper Menu");
        scraperMenu.AddAction("Parse Stem Products", actions.ParseStemProductsAsync);

        MenuManager.AddAction("Authorize", actions.PerformAuthorizationAsync);
        MenuManager.SetExitOption("Exit");

        Log.Print("Menu initialized successfully.");
    }

    private static async Task ConfigureBrowserContexts(ChromiumScraper scraper)
    {
        Log.Print("Configuring browser contexts...");
        
        // Контекст для навигации (только HTML)
        var catalogContext = await scraper.CreateContextAsync(Constants.Contexts.CatalogParser);
        await catalogContext.RouteAsync("**/*", static route =>
        {
            if (route.Request.ResourceType is not "document") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Контекст для продуктов (HTML и картинки)
        var productContext = await scraper.CreateContextAsync(Constants.Contexts.ProductParser);
        await productContext.RouteAsync("**/*", static route =>
        {
            var type = route.Request.ResourceType;
            if (type is not "document" and not "image") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Контекст для редактора (HTML и картинки)
        var editorContext = await scraper.CreateContextAsync(Constants.Contexts.Editor);
        await editorContext.RouteAsync("**/*", static route =>
        {
            var type = route.Request.ResourceType;
            if (type is not "document" and not "image" and not "script" and not "xhr") route.AbortAsync();
            else route.ContinueAsync();
        });

        Log.Print("Contexts configured successfully.");
    }
}