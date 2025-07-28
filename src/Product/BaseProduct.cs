namespace ScraperAcesso.Product;

using ScraperAcesso.Ai;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Utils;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using IOPath = System.IO.Path;

public class BaseProduct(in string title, in Uri url, in int price = BaseProduct.DefaultPrice, in int count = BaseProduct.DefaultCount)
{
    public const ushort DefaultCount = 9999;
    public const ushort DefaultPrice = 0;
    public const ushort MaxTitleLength = 256;
    public const ushort MaxShortDescriptionLength = 1000;
    public const ushort MaxImagesCount = 13;

    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Title { get; protected set; } = title;
    public string TranslitedTitle => Transliterator.ToSafeId(Title);
    public Uri URL { get; protected set; } = url;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public int Price { get; set; } = price;
    public int Count { get; protected set; } = count;
    public SEOProductInfo? SEO { get; set; }

    public List<ProductAttribute> Attributes { get; set; } = new();

    public List<string>? AllImages { get; set; }
    public string? PreviewImage => AllImages?.FirstOrDefault();

    public string FolderPath => IOPath.Combine(Constants.Path.Folder.Products, TranslitedTitle ?? "UNKNOWN");
    public string DataFilePath => IOPath.Combine(FolderPath, Constants.Path.Name.File.ProductData);
    public string ImageFolderPath => IOPath.Combine(FolderPath, Constants.Path.Name.Folder.ProductImages);
    public string AddedMarkerFilePath => IOPath.Combine(FolderPath, Constants.Path.Name.File.ProductMarkerAdded);
    public string ValidationErrorFilePath => IOPath.Combine(FolderPath, "validation_error.json");

    /// <summary>
    /// Проверяет, был ли продукт отмечен как "добавленный".
    /// </summary>
    public bool IsAdded => File.Exists(AddedMarkerFilePath);

    /// <summary>
    /// Проверяет, был ли продукт отмечен как "невалидный" из-за ошибок данных.
    /// </summary>
    public bool IsInvalid => File.Exists(ValidationErrorFilePath);

    /// <summary>
    /// Асинхронно помечает продукт как "добавленный", создавая пустой файл-маркер.
    /// </summary>
    public async Task<bool> MarkAsAddedAsync()
    {
        try
        {
            // Создаем файл. Содержимое не важно, важно его наличие.
            await File.WriteAllTextAsync(AddedMarkerFilePath, DateTime.UtcNow.ToString("o"));
            Log.Print($"Product '{Title}' marked as added.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to mark product '{Title}' as added. Error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Асинхронно снимает с продукта отметку "добавленный", удаляя файл-маркер.
    /// </summary>
    public bool UnmarkAsAdded()
    {
        if (!File.Exists(AddedMarkerFilePath)) return true;

        try
        {
            File.Delete(AddedMarkerFilePath);
            Log.Print($"Product '{Title}' unmarked as added.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to unmark product '{Title}'. Error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Асинхронно помечает продукт как невалидный, создавая файл с деталями ошибок.
    /// </summary>
    public async Task<bool> MarkAsInvalidAsync(List<ValidationError> errors)
    {
        try
        {
            var errorInfo = new ValidationErrorFile(DateTime.UtcNow, errors);
            var jsonContent = JsonSerializer.Serialize(errorInfo, JsonSerializerOptions);
            await File.WriteAllTextAsync(ValidationErrorFilePath, jsonContent, Encoding.UTF8);
            Log.Error($"Product '{Title}' marked as INVALID. Details saved to '{ValidationErrorFilePath}'.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"CRITICAL: Failed to mark product '{Title}' as invalid. Error: {ex.Message}");
        }

        return false;
    }

    public bool UnmarkAsInvalid()
    {
        if (!File.Exists(ValidationErrorFilePath)) return true;

        try
        {
            File.Delete(ValidationErrorFilePath);
            Log.Print($"Product '{Title}' unmarked as invalid.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to unmark product '{Title}'. Error: {ex.Message}");
        }

        return false;
    }

    public string GetAttributesAsString(string separator = " — ")
    {
        if (Attributes == null || Attributes.Count == 0)
        {
            return string.Empty;
        }

        Log.Print($"Get attributes with separator '{separator}'");
        return string.Join("\n", Attributes.Select(attr => $"{attr.Name.Trim()}{separator}{attr.Value.Trim()}"));
    }

    /// <summary>
    /// Асинхронно сохраняет основные данные продукта в файл data.json.
    /// </summary>
    /// <returns>True в случае успеха, иначе False.</returns>
    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Log.Warning($"Cannot save product with an empty title. URL: {URL}");
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(TranslitedTitle))
            {
                Log.Error($"Failed to generate a valid folder name for the product: {Title}");
                return false;
            }

            Directory.CreateDirectory(ImageFolderPath);

            var dataToSave = new
            {
                Title,
                URL,
                Price,
                Count,
                Description,
                ShortDescription,
                SEO,
                ImagePaths = AllImages,
                Attributes,
            };

            var jsonContent = JsonSerializer.Serialize(dataToSave, JsonSerializerOptions);
            await File.WriteAllTextAsync(DataFilePath, jsonContent, Encoding.UTF8);

            Log.Print($"Product data for '{Title}' saved to '{DataFilePath}'");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save product '{Title}'. Details: {ex}");
            return false;
        }
    }

    public static bool HasProductIO(in string translitedTitle)
    {
        return File.Exists(IOPath.Combine(Constants.Path.Folder.Products, translitedTitle, Constants.Path.Name.File.ProductData));
    }

    /// <summary>
    /// Производительно загружает продукты, фильтруя их по статусу "добавлен".
    /// Загрузка происходит параллельно для максимальной скорости.
    /// </summary>
    /// <param name="addedStatus">
    /// True - загрузить только добавленные.
    /// False - загрузить только НЕ добавленные.
    /// Null - загрузить все продукты.
    /// </param>
    /// <returns>Список загруженных продуктов.</returns>
    public static async Task<List<BaseProduct>> LoadProductsByStatusAsync(bool? addedStatus)
    {
        if (!Directory.Exists(Constants.Path.Folder.Products))
        {
            Log.Warning($"Products directory '{Constants.Path.Folder.Products}' not found. No products to load.");
            return [];
        }

        var productDirectories = Directory.GetDirectories(Constants.Path.Folder.Products);

        // Используем ConcurrentBag для потокобезопасного сбора результатов.
        var loadedProducts = new ConcurrentBag<BaseProduct>();

        // Создаем список задач для параллельного выполнения
        var loadingTasks = new List<Task>();

        foreach (var dir in productDirectories)
        {
            // --- ЭФФЕКТИВНАЯ ФИЛЬТРАЦИЯ ---
            if (addedStatus.HasValue)
            {
                var markerPath = IOPath.Combine(dir, Constants.Path.Name.File.ProductMarkerAdded);
                bool isActuallyAdded = File.Exists(markerPath);

                if (isActuallyAdded != addedStatus.Value)
                {
                    continue; // Пропускаем, если статус "добавлен" не совпадает
                }

                // --- НОВАЯ ЛОГИКА ---
                // Если мы ищем НЕ добавленные (pending), то также надо исключить невалидные.
                if (addedStatus.Value == false)
                {
                    var invalidMarkerPath = IOPath.Combine(dir, "validation_error.json");
                    if (File.Exists(invalidMarkerPath))
                    {
                        continue; // Пропускаем невалидные товары
                    }
                }
            }

            // Если фильтр пройден, добавляем задачу на загрузку этого продукта в список.
            loadingTasks.Add(Task.Run(async () =>
            {
                var product = await LoadFromDirectoryAsync(dir);
                if (product != null)
                {
                    loadedProducts.Add(product);
                }
            }));
        }

        Log.Print($"Found {loadingTasks.Count} products matching the filter. Starting parallel loading...");

        // Ждем завершения всех задач по загрузке
        await Task.WhenAll(loadingTasks);

        Log.Print($"Successfully loaded {loadedProducts.Count} products.");
        return [.. loadedProducts];
    }

    /// <summary>
    /// Загружает все продукты, которые были помечены как невалидные, вместе с информацией об их ошибках.
    /// </summary>
    /// <returns>Словарь, где ключ - невалидный продукт, а значение - информация об ошибках.</returns>
    public static async Task<Dictionary<BaseProduct, ValidationErrorFile>> LoadInvalidProductsAsync()
    {
        if (!Directory.Exists(Constants.Path.Folder.Products)) return [];

        var invalidProducts = new ConcurrentDictionary<BaseProduct, ValidationErrorFile>();
        var productDirectories = Directory.GetDirectories(Constants.Path.Folder.Products);

        await Parallel.ForEachAsync(productDirectories, async (dir, token) =>
        {
            var errorFilePath = IOPath.Combine(dir, "validation_error.json");
            if (!File.Exists(errorFilePath)) return;

            try
            {
                var product = await LoadFromDirectoryAsync(dir);
                if (product == null) return;

                var jsonContent = await File.ReadAllTextAsync(errorFilePath, token);
                var errorInfo = JsonSerializer.Deserialize<ValidationErrorFile>(jsonContent, JsonSerializerOptions);

                if (errorInfo != null)
                {
                    invalidProducts.TryAdd(product, errorInfo);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load invalid product info from '{dir}'. Reason: {ex.Message}");
            }
        });

        Log.Print($"Found {invalidProducts.Count} invalid products.");
        return new Dictionary<BaseProduct, ValidationErrorFile>(invalidProducts);
    }

    /// <summary>
    /// Загружает все продукты из файловой системы.
    /// Для обратной совместимости и удобства.
    /// </summary>
    public static Task<List<BaseProduct>> LoadAllAsync()
    {
        return LoadProductsByStatusAsync(null);
    }

    /// <summary>
    /// Вспомогательный метод для загрузки одного продукта из его директории.
    /// </summary>
    /// <returns>Экземпляр BaseProduct или null в случае ошибки.</returns>
    private static async Task<BaseProduct?> LoadFromDirectoryAsync(string directoryPath)
    {
        var dataFilePath = IOPath.Combine(directoryPath, Constants.Path.Name.File.ProductData);

        if (!File.Exists(dataFilePath))
        {
            Log.Warning($"Skipping directory '{directoryPath}' because 'data.json' was not found.");
            return null;
        }

        try
        {
            var jsonContent = await File.ReadAllTextAsync(dataFilePath);
            var tempProductData = JsonSerializer.Deserialize<TemporaryProductData>(jsonContent, JsonSerializerOptions);

            if (tempProductData == null || string.IsNullOrWhiteSpace(tempProductData.Title) || string.IsNullOrWhiteSpace(tempProductData.URL))
            {
                Log.Warning($"Failed to deserialize or product title/URL is empty '{dataFilePath}'.");
                return null;
            }

            return new BaseProduct(tempProductData.Title, new Uri(tempProductData.URL), tempProductData.Price, tempProductData.Count)
            {
                Description = tempProductData.Description,
                ShortDescription = tempProductData.ShortDescription,
                SEO = tempProductData.SEO,
                AllImages = tempProductData.ImagePaths,
                Attributes = tempProductData.Attributes ?? []
            };
        }
        catch (JsonException jsonEx)
        {
            Log.Error($"Failed to parse JSON '{dataFilePath}'. Error: {jsonEx.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"An unexpected error occurred while loading product from '{dataFilePath}'. Error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> EnqueueGenerateAiDataAsync()
    {
        if (SettingsManager.Instance.GetAutoSeoEnabled() && !string.IsNullOrWhiteSpace(Description))
        {
            Log.Print($"Auto-SEO is enabled. Generating content for '{URL}'...");
            await GeminiBatchProcessor.EnqueueAsync(this);
            return true;
        }

        return false;
    }

    // Вспомогательный класс для десериализации.
    private sealed class TemporaryProductData
    {
        public string Title { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public int Price { get; set; }
        public int Count { get; set; }
        public string? Description { get; set; }
        public string? ShortDescription { get; set; }
        public SEOProductInfo? SEO { get; set; }
        public List<string>? ImagePaths { get; set; }
        public List<ProductAttribute>? Attributes { get; set; }
    }
}