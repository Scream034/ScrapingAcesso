using ScraperAcesso.Components.Log;
using ScraperAcesso.Utils;
using System.Text.Json;

namespace ScraperAcesso.Product;

public class BaseProduct
{
    // Константы и статические свойства остаются
    public const int DefaultCount = 9999;
    public const int DefaultPrice = 0;

    // Публичные свойства для данных
    public string Title { get; private set; }
    public string URL { get; private set; }
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public int Price { get; set; }
    public int Count { get; private set; } = 9999;
    public SEOProductInfo? SEO { get; set; }
    public string[]? AllImages { get; set; }

    // НОВОЕ СВОЙСТВО: Характеристики
    public List<ProductAttribute> Attributes { get; set; } = new();

    public string? PreviewImage => AllImages?.FirstOrDefault();

    public BaseProduct(in string title, in string url, in int price = DefaultPrice, in int count = DefaultCount)
    {
        Title = title;
        URL = url;
        Price = price;
        Count = count;
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
            var productFolderName = Transliterator.ToSafeId(Title);
            if (string.IsNullOrWhiteSpace(productFolderName))
            {
                // Если заголовок состоит только из спецсимволов, используем хеш URL
                productFolderName = $"product-{URL.GetHashCode():x}";
                Log.Warning($"Generated fallback folder name '{productFolderName}' for product with title '{Title}'.");
            }

            var productPath = Path.Combine(Constants.Path.Folder.Products, productFolderName);

            // 2. Создаем директории для продукта и изображений
            Directory.CreateDirectory(productPath);
            Directory.CreateDirectory(Path.Combine(productPath, Constants.Path.Name.Folder.ProductImages));

            // 3. Создаем объект для сохранения в JSON, исключая некоторые свойства
            // Это позволяет контролировать, что именно попадает в файл.
            var dataToSave = new
            {
                Title,
                URL,
                Price,
                Count,
                Description,
                ShortDescription,
                SEO,
                ImagePaths = AllImages, // Сохраняем ссылки на оригинальные изображения
                Attributes
            };

            // 4. Сериализуем и сохраняем в JSON
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonContent = JsonSerializer.Serialize(dataToSave, jsonOptions);

            await File.WriteAllTextAsync(Path.Combine(productPath, Constants.Path.Name.File.ProductData), jsonContent);

            Log.Print($"Product '{Title}' saved to '{productPath}'");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save product '{Title}'. Error: {ex.Message}");
            // Log.Error($"Details: {ex}"); // Раскомментировать для полной трассировки
            return false;
        }
    }

    /// <summary>
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
            var dataFilePath = Path.Combine(dir, "data.json");

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
                var product = new BaseProduct(tempProductData.Title, tempProductData.URL, tempProductData.Price, tempProductData.Count)
                {
                    // Заполняем остальные свойства
                    Description = tempProductData.Description,
                    ShortDescription = tempProductData.ShortDescription,
                    SEO = tempProductData.SEO,
                    AllImages = tempProductData.ImagePaths,
                    Attributes = tempProductData.Attributes ?? new List<ProductAttribute>()
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
        // Имя свойства должно совпадать с тем, что мы указали при сохранении ("ImagePaths")
        public string[]? ImagePaths { get; set; }
        public List<ProductAttribute>? Attributes { get; set; }
    }
}