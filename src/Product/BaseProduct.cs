namespace ScraperAcesso.Product;

using ScraperAcesso.Components.Log;
using ScraperAcesso.Utils;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using IOPath = System.IO.Path;

public class BaseProduct(in string title, in Uri url, in int price = BaseProduct.DefaultPrice, in int count = BaseProduct.DefaultCount)
{
    public const int DefaultCount = 9999;
    public const int DefaultPrice = 0;
    public const uint MaxTitleLength = 256;
    public const uint MaxShortDescriptionLength = 1000;

    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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

    public bool IsAdded => File.Exists(IOPath.Combine(FolderPath, Constants.Path.Name.File.ProductMarkerAdded));

    public string FolderPath => IOPath.Combine(Constants.Path.Folder.Products, TranslitedTitle ?? "UNKOWN");
    public string DataFilePath => IOPath.Combine(FolderPath, Constants.Path.Name.File.ProductData);
    public string ImageFolderPath => IOPath.Combine(FolderPath, Constants.Path.Name.Folder.ProductImages);
    public string AddedMarkerFilePath => IOPath.Combine(FolderPath, Constants.Path.Name.File.ProductMarkerAdded);

    public async Task MarkAsAddedAsync()
    {
        try
        {
            await File.WriteAllTextAsync(AddedMarkerFilePath, DateTime.UtcNow.ToString("o"));
            Log.Print($"Product '{Title}' marked as added.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to mark product '{Title}' as added. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Asynchronously saves the product data to the disk in a structured folder.
    /// </summary>
    /// <returns>True if successful, false otherwise.</returns>
    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            Log.Warning($"Cannot save product with an empty title. URL: {URL}");
            return false;
        }

        try
        {
            // 1. Создаем безопасное имя папки из заголовка
            if (string.IsNullOrWhiteSpace(TranslitedTitle))
            {
                Log.Error($"Failed to generate a valid folder name for the product: {Title}");
                return false;
            }

            // 2. Создаем директории для продукта и изображений
            Directory.CreateDirectory(ImageFolderPath);

            // 3. Создаем объект для сохранения в JSON, исключая некоторые свойства
            // Это позволяет контролировать, что именно попадает в файл.
            var dataToSave = new
            {
                Title,
                URL = URL.ToString(),
                Price,
                Count,
                Description,
                ShortDescription,
                SEO,
                ImagePaths = AllImages, // Сохраняем ссылки на оригинальные изображения
                Attributes,
                IsAdded,
            };

            // 4. Сериализуем и сохраняем в JSON
            var jsonContent = JsonSerializer.Serialize(dataToSave, JsonSerializerOptions);
            await File.WriteAllTextAsync(DataFilePath, jsonContent, Encoding.UTF8);

            Log.Print($"Product '{Title}' saved to '{DataFilePath}'");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save product '{Title}'.");
            Log.Error($"Details: {ex}"); // Раскомментировать для полной трассировки
            return false;
        }
    }

    /// Asynchronously loads all products from the base products folder.
    /// </summary>
    /// <returns>A list of loaded BaseProduct objects.</returns>
    public static async Task<List<BaseProduct>> LoadAllAsync()
    {
        var loadedProducts = new List<BaseProduct>();

        // Проверяем, существует ли базовая папка с продуктами
        if (!Directory.Exists(Constants.Path.Folder.Products))
        {
            Log.Warning($"Products directory '{Constants.Path.Folder.Products}' not found. No products to load.");
            return loadedProducts;
        }

        // Получаем все подпапки в директории products
        var productDirectories = Directory.GetDirectories(Constants.Path.Folder.Products);
        Log.Print($"Found {productDirectories.Length} product directories. Starting to load...");

        foreach (var dir in productDirectories)
        {
            var dataFilePath = IOPath.Combine(dir, "data.json");

            // Проверяем, существует ли файл data.json
            if (!File.Exists(dataFilePath))
            {
                Log.Warning($"Skipping directory '{dir}' because 'data.json' was not found.");
                continue;
            }

            try
            {
                // Асинхронно читаем содержимое файла
                var jsonContent = await File.ReadAllTextAsync(dataFilePath);

                // Десериализуем JSON в анонимный тип, который соответствует структуре сохранения
                // Это более надежно, чем пытаться десериализовать напрямую в BaseProduct,
                // так как конструктор BaseProduct требует параметров.
                var tempProductData = JsonSerializer.Deserialize<TemporaryProductData>(jsonContent);

                if (tempProductData == null || string.IsNullOrWhiteSpace(tempProductData.Title))
                {
                    Log.Warning($"Failed to deserialize or product title is empty in '{dataFilePath}'.");
                    continue;
                }

                // Создаем экземпляр BaseProduct, используя конструктор
                var product = new BaseProduct(tempProductData.Title, new(tempProductData.URL), tempProductData.Price, tempProductData.Count)
                {
                    // Заполняем остальные свойства
                    Description = tempProductData.Description,
                    ShortDescription = tempProductData.ShortDescription,
                    SEO = tempProductData.SEO,
                    AllImages = tempProductData.ImagePaths,
                    Attributes = tempProductData.Attributes ?? []
                };

                loadedProducts.Add(product);
            }
            catch (JsonException jsonEx)
            {
                Log.Error($"Failed to parse JSON in '{dataFilePath}'. Error: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                Log.Error($"An unexpected error occurred while loading product from '{dataFilePath}'. Error: {ex.Message}");
            }
        }

        Log.Print($"Successfully loaded {loadedProducts.Count} products.");
        return loadedProducts;
    }

    // Вспомогательный класс для десериализации. Он должен быть определен вне `BaseProduct`,
    // но в том же файле или в отдельном файле моделей.
    // Я помещу его здесь для простоты.
    // Имена свойств должны ТОЧНО соответствовать именам в JSON при сохранении!
    private class TemporaryProductData
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
        public bool IsAdded { get; set; }
    }
}