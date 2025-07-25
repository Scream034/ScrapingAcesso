using IOPath = System.IO.Path;

namespace ScraperAcesso;

public static class Constants
{
    public const string AppName = "ScrapingAcesso";

    public static class Path
    {
        public static class File
        {
            public static readonly string Settings = IOPath.Combine(Folder.App, Name.File.Settings);
            public static readonly string Log = IOPath.Combine(Folder.Log, Name.File.Log);
            public static readonly string PlaywrightPS1 = Name.File.PlaywrightPS1;
            public static readonly string GemeniModelConfig = IOPath.Combine(Folder.App, Name.File.GemeniModelConfig);
            public static readonly string GemeniModelState = IOPath.Combine(Folder.App, Name.File.GemeniModelState);
        }

        public static class Folder
        {
            /// <summary>
            /// Path to the application directory.
            /// </summary>
            public static readonly string App = IOPath.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            public static readonly string Log = IOPath.Combine(App, Name.Folder.Log);
            public static readonly string Products = IOPath.Combine(App, Name.Folder.Products);
        }

        public static class Name
        {
            public static class File
            {
                public static readonly string Settings = "settings.json";
                public static readonly string Log = generateLogFileName();
                public static readonly string PlaywrightPS1 = "playwright.ps1";
                public static readonly string ProductData = "data.json";
                public static readonly string ProductMarkerAdded = ".added";
                public static readonly string GemeniModelConfig = "gemini_models.json";
                public static readonly string GemeniModelState = "gemini_state.json";
            }

            public static class Folder
            {
                public static readonly string Log = "logs";
                public static readonly string Products = "products";
                public static readonly string ProductImages = "images";
            }
        }
    }

    /// <summary>
    /// Используется для идентификации контекстов при работе с ChromiumScraper.
    /// </summary>
    public static class Contexts
    {
        public const string Editor = "Ed";
        public const string CatalogParser = "CP";
        public const string ProductParser = "PP";
    }

    /// <summary>
    /// Генерирует имя файла для лога с текущей датой и временем.
    /// </summary>
    /// <returns>Имя файла для лога.</returns>
    private static string generateLogFileName()
    {
        return $"{Microsoft.VisualBasic.DateAndTime.Now:yyyyMMdd_HHmmss}_log.log";
    }

    /// <summary>
    /// Проверяет и создает необходимые директории для логов.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        string[] directories = { Path.Folder.Log, Path.Folder.Products };
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}