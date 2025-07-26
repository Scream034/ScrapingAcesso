namespace ScraperAcesso;

using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Menu;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Utils;

public static class Program
{
    public static readonly ConsoleMenuManager MenuManager = new("Main Menu");

    public static async Task Main(string[] args)
    {
        Constants.EnsureDirectoriesExist();
        Log.Initialize();
        QueuedImageDownloader.Initialize();

        Log.Print($"Starting {Constants.AppName} v2.5...");

        // Handle Ctrl+C (graceful shutdown)
        Console.CancelKeyPress += static async (_, eventArgs) =>
        {
            Log.Warning("Ctrl+C pressed. Disposing resources...");
            eventArgs.Cancel = true;
            await ShutdownServicesAsync();
            Environment.Exit(0);
        };

        try
        {
            await HandleMain(args);
        }
        catch (Exception ex)
        {
            Log.Error($"An unhandled exception occurred in Main: {ex}");
        }
        finally
        {
            // Dispose services
            await ShutdownServicesAsync();
            Log.Print("Application has shut down.");
        }
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

    private static async Task ShutdownServicesAsync()
    {
        await GeminiBatchProcessor.ShutdownAsync();
        await QueuedImageDownloader.ShutdownAsync();
        GeminiService.Shutdown(); // Сохраняем состояние лимитов
        Log.Dispose();
    }

    private static void InitializeMenu(AppActions actions)
    {
        Log.Print("Initializing menu...");

        var scraperMenu = MenuManager.AddSubMenu("Scraper Menu");
        scraperMenu.AddAction("Parse Stem Products", actions.ParseStemProductsAsync);

        var editorMenu = MenuManager.AddSubMenu("Editor Mode");
        editorMenu.AddAction("Add Products to Site", actions.RunEditorModeAsync);

        var aiMenu = MenuManager.AddSubMenu("AI Actions");
        aiMenu.AddAction("Generate SEO for all products", actions.GenerateSeoForAllRawProductsAsync);

        var testMenu = MenuManager.AddSubMenu("Tests");
        testMenu.AddAction("Test AI Generation (Single)", actions.TestAiGenerationAsync);
        testMenu.AddAction("Test AI Generation (Batch)", actions.TestAiBatchGenerationAsync);
        testMenu.AddAction("Run Full E2E Cycle Test", actions.RunFullCycleTestAsync);
        testMenu.AddAction("Test Single Product Addition (Auto-Login)", actions.RunSingleProductEditorTestAsync);

        var settingsMenu = MenuManager.AddSubMenu("Settings");
        settingsMenu.AddAction("Configure Gemini API Key", actions.ConfigureGeminiApiKey);
        settingsMenu.AddAction("Toggle Auto-SEO on Parse", actions.ToggleAutoSeoGeneration);

        MenuManager.AddAction("Authorize", actions.PerformAuthorizationAsync);
        MenuManager.SetExitOption("Exit");

        Log.Print("Menu initialized successfully.");
    }

    private static async Task ConfigureBrowserContexts(ChromiumScraper scraper)
    {
        Log.Print("Configuring browser contexts with animation disabling...");

        // --- CSS-правила, которые мы хотим внедрить ---
        const string styles = @"
            *, *::before, *::after {
              -webkit-transition: none !important;
              -moz-transition: none !important;
              -o-transition: none !important;
              -ms-transition: none !important;
              transition: none !important;
              -webkit-animation: none !important;
              -moz-animation: none !important;
              -o-animation: none !important;
              -ms-animation: none !important;
              animation: none !important;
              scroll-behavior: auto !important;
              transition-delay: 0s !important;
              animation-delay: 0s !important;
            }";

        // --- JAVASCRIPT-КОД, КОТОРЫЙ ВНЕДРЯЕТ НАШИ СТИЛИ В <HEAD> ---
        // Он создает тег <style>, наполняет его нашим CSS и добавляет в DOM.
        // Это гарантирует, что стили применятся до полной загрузки страницы.
        const string scriptToInject = $$"""
            console.log('Animation-disabling script injecting...');
            document.addEventListener('DOMContentLoaded', function() 
            {
                console.log('Document loaded, injecting styles...');
                const style = document.createElement('style');
                style.type = 'text/css';
                style.innerHTML = `{{styles}}`;
                document.head.append(style);
                console.log('Animation-disabling script injected', style);
            });
        """;
        // -----------------------------------------------------------------

        // Context for catalog (HTML)
        var catalogContext = await scraper.CreateContextAsync(Constants.Contexts.CatalogParser);
        await catalogContext.AddInitScriptAsync(scriptToInject);
        await catalogContext.RouteAsync("**/*", static route =>
        {
            if (route.Request.ResourceType is not "document") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Context for product (HTML)
        var productContext = await scraper.CreateContextAsync(Constants.Contexts.ProductParser);
        await productContext.AddInitScriptAsync(scriptToInject);
        await productContext.RouteAsync("**/*", static route =>
        {
            var type = route.Request.ResourceType;
            if (type is not "document" and not "image") route.AbortAsync();
            else route.ContinueAsync();
        });

        // Context for editor (HTML)
        var editorContext = await scraper.CreateContextAsync(Constants.Contexts.Editor);
        await editorContext.AddInitScriptAsync(scriptToInject);

        Log.Print("Contexts configured successfully. Animation-disabling script will be injected into every page.");
    }
}