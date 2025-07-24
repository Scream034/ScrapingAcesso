using IOPath = System.IO.Path;

namespace ScraperAcesso;

public static class Constants
{
    public const string AppName = "ScrapingAcesso";

    public static class Path
    {
        public static class File
        {
            public static readonly string Log = IOPath.Combine(Folder.Log, Name.File.Log);
            public static readonly string PlaywrightPS1 = Name.File.PlaywrightPS1;
        }

        public static class Folder
        {
            public const string App = $"{AppName}";
            public static readonly string Log = Name.Folder.Log;
            public static readonly string Products = IOPath.Combine(App, Name.Folder.Products);
        }

        public static class Name
        {
            public static class File
            {
                public static readonly string Log = generateLogFileName();
                public static readonly string PlaywrightPS1 = "playwright.ps1";
                public static readonly string ProductData = "data.json";
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
        string[] directories = { Path.Folder.Log };
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}