namespace ScraperAcesso;

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
        Log.Print($"Starting {Constants.AppName} v2.0...");

        // Handle Ctrl+C (Quit)
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
        // 1. Initialize services
        Log.Print("Initializing services...");
        var settingsManager = new SettingsManager();
        settingsManager.Load();

        await using var scraper = await ChromiumScraper.CreateAsync();
        var appActions = new AppActions(scraper, settingsManager);

        // 2. Configure browser contexts
        await ConfigureBrowserContexts(scraper);

        // 3. Create and run menu
        InitializeMenu(appActions);
        await MenuManager.RunAsync();

        Log.Print("Menu loop finished. Exiting application.");
    }

    private static void InitializeMenu(AppActions actions)
    {
        Log.Print("Initializing menu...");

        // Scraper sub menu
        var scraperMenu = MenuManager.AddSubMenu("Scraper Menu");
        scraperMenu.AddAction("Parse Stem Products", actions.ParseStemProductsAsync);

        // Ai sub menu
        var aiMenu = MenuManager.AddSubMenu("AI Actions");
        aiMenu.AddAction("Generate SEO for all products", actions.GenerateSeoForAllProductsAsync);

        // Tests sub menu
        var testMenu = MenuManager.AddSubMenu("Tests");
        testMenu.AddAction("Test AI Generation", actions.TestAiGenerationAsync);

        MenuManager.AddAction("Authorize", actions.PerformAuthorizationAsync);
        MenuManager.AddAction("Configure Gemini API Key", actions.ConfigureGeminiApiKey);
        MenuManager.SetExitOption("Exit");

        Log.Print("Menu initialized successfully.");
    }
    private static async Task ConfigureBrowserContexts(ChromiumScraper scraper)
    {
        Log.Print("Configuring browser contexts...");

        // Context for catalog (HTML)
        var catalogContext = await scraper.CreateContextAsync(Constants.Contexts.CatalogParser);
        await catalogContext.RouteAsync("**/*", static route =>
        {
            if (route.Request.ResourceType is not "document") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Context for product (HTML)
        var productContext = await scraper.CreateContextAsync(Constants.Contexts.ProductParser);
        await productContext.RouteAsync("**/*", static route =>
        {
            var type = route.Request.ResourceType;
            if (type is not "document" and not "image") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Context for editor (HTML)
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