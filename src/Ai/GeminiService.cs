namespace ScraperAcesso.Ai;

using GenerativeAI;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product;
using System.Text;

/// <summary>
/// Статический сервис для взаимодействия с Google Gemini API.
/// Делегирует управление моделями и лимитами классу GeminiModelManager.
/// </summary>
public static class GeminiService
{
    // Менеджер моделей, который управляет конфигурацией и состоянием лимитов.
    private static GeminiModelManager? s_modelManager;
    private static string? s_apiKey;

    // Семафор для гарантии того, что только один БАТЧ обрабатывается в данный момент.
    private static readonly SemaphoreSlim s_requestGate = new(1, 1);

    /// <summary>
    /// Инициализирует сервис с API ключом и запускает менеджер моделей.
    /// </summary>
    public static bool Initialize(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Error("Gemini API key is not provided.");
            return false;
        }

        s_apiKey = apiKey;

        // Инициализируем и запускаем менеджер моделей.
        s_modelManager = new GeminiModelManager();
        s_modelManager.Initialize();

        return true;
    }

    /// <summary>
    /// Сохраняет состояние лимитов перед выходом.
    /// </summary>
    public static void Shutdown()
    {
        Log.Print("Shutting down Gemini Service...");
        s_modelManager?.SaveState();
    }

    /// <summary>
    /// Генерирует контент для коллекции продуктов (батча).
    /// </summary>
    public static async Task<bool> GenerateContentForBatchAsync(ICollection<BaseProduct> products)
    {
        if (products.Count == 0) return true;

        if (s_modelManager == null || string.IsNullOrWhiteSpace(s_apiKey))
        {
            Log.Error("Gemini Service is not initialized.");
            return false;
        }

        await s_requestGate.WaitAsync();

        try
        {
            const int maxRetries = 5; // Увеличим количество попыток на случай временной недоступности всех моделей
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // 1. Получаем следующую доступную модель от менеджера
                var model = s_modelManager.GetNextAvailableModel();

                if (model == null)
                {
                    Log.Warning($"Attempt {attempt}/{maxRetries}: No models available. Waiting for 30 seconds...");
                    await Task.Delay(30_000);
                    continue; // Переходим к следующей попытке
                }

                Log.Print($"Attempt {attempt}/{maxRetries}: Using model '{model.ApiName}' for batch processing.");

                try
                {
                    // 2. Создаем клиент для конкретной модели
                    var generativeModel = new GenerativeModel(s_apiKey, model.ApiName);
                    var prompt = BuildBatchPrompt(products);
                    var rawResponse = await generativeModel.GenerateContentAsync(prompt);

                    // 3. Парсим ответ и обновляем продукты
                    ParseBatchResponse(rawResponse.Text, products);

                    // 4. Записываем успешный запрос в менеджер
                    s_modelManager.RecordRequest(model.ApiName);

                    return true; // Успех!
                }
                catch (Exception ex) when (ex.Message.Contains("RESOURCE_EXHAUSTED") || ex.Message.Contains("429"))
                {
                    // Эта ошибка все еще может произойти, если лимит был достигнут между проверкой и запросом.
                    // Регистрируем это как запрос, чтобы менеджер знал о проблеме.
                    Log.Warning($"Rate limit hit for model '{model.ApiName}' unexpectedly. Recording it and retrying with another model.");
                    s_modelManager.RecordRequest(model.ApiName);
                    // Просто переходим к следующей итерации цикла, чтобы попробовать другую модель.
                }
                catch (Exception ex)
                {
                    Log.Error($"An unhandled error occurred during Gemini API batch request with model '{model.ApiName}'. Error: {ex.Message}");
                    // Для других ошибок выходим, чтобы не тратить лимиты.
                    return false;
                }
            }

            Log.Error("Failed to process batch after multiple retries. All models might be exhausted or an error occurred.");
            return false;
        }
        finally
        {
            s_requestGate.Release();
        }
    }

    public static async Task<bool> GenerateContentAsync(BaseProduct product)
    {
        // Просто оборачиваем одиночный продукт в батч из одного элемента
        return await GenerateContentForBatchAsync([product]);
    }

    private static string BuildBatchPrompt(ICollection<BaseProduct> products)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ТЫ — ПРОФЕССИОНАЛЬНЫЙ SEO-КОПИРАЙТЕР. Твоя задача — создать контент для {products.Count} карточек товаров. Ответ должен быть на русском языке.");
        sb.AppendLine("Для каждого товара предоставлены его ID (URL), Название и Описание.");

        sb.AppendLine("\n--- ИСХОДНЫЕ ДАННЫЕ ---");
        foreach (var product in products)
        {
            sb.AppendLine("$$$===PRODUCT_START===$$$");
            sb.AppendLine($"ID: {product.URL}"); // URL as ID
            sb.AppendLine($"Название товара: {product.Title}");
            sb.AppendLine("Полное описание товара:");
            sb.AppendLine(product.Description);
            sb.AppendLine("$$$===PRODUCT_END===$$$");
        }

        sb.AppendLine("\n--- ЗАДАНИЕ ---");
        sb.AppendLine("Для КАЖДОГО товара выше сгенерируй ТРИ блока текста:");
        sb.AppendLine($"1.  **Краткое описание:** Рерайт. Длина: до {BaseProduct.MaxShortDescriptionLength} симв.");
        sb.AppendLine($"2.  **SEO-предложение:** Одно предложение. Длина: до {SEOProductInfo.MaxSeoSentenceLength} симв.");
        sb.AppendLine($"3.  **Ключевые слова:** 5-7 слов через запятую. Длина: до {SEOProductInfo.MaxKeywordsLength} симв.");

        sb.AppendLine("\n--- ТРЕБОВАНИЯ К ФОРМАТУ ОТВЕТА ---");
        sb.AppendLine("Твой ответ должен содержать ТОЛЬКО сгенерированный контент для каждого товара.");
        sb.AppendLine("Ответ для каждого товара должен начинаться с его ID и следовать СТРОГОЙ структуре.");
        sb.AppendLine("Разделяй полные ответы для разных товаров строкой '%%%===ITEM_SEPARATOR===%%%'.");
        sb.AppendLine("Пример правильной структуры ответа для ДВУХ товаров:");
        sb.AppendLine("ID: http://example.com/product/1");
        sb.AppendLine("[Сгенерированный текст краткого описания для товара 1]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированное SEO-предложение для товара 1]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированные ключевые слова для товара 1]");
        sb.AppendLine("%%%===ITEM_SEPARATOR===%%%");
        sb.AppendLine("ID: http://example.com/product/2");
        sb.AppendLine("[Сгенерированный текст краткого описания для товара 2]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированное SEO-предложение для товара 2]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированные ключевые слова для товара 2]");

        return sb.ToString();
    }

    private static void ParseBatchResponse(string responseText, ICollection<BaseProduct> products)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Log.Error("Gemini API returned an empty response for a batch.");
            return;
        }

        var productResponses = responseText.Split(["%%%===ITEM_SEPARATOR===%%%"], StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < productResponses.Length; i++)
        {
            var singleProductResponse = productResponses[i];
            var lines = singleProductResponse.Trim().Split('\n');
            if (!lines[0].StartsWith("ID: "))
            {
                Log.Warning($"Could not find product ID in a batch response part. Part ({i}/{productResponses.Length}): '{singleProductResponse}'");
                continue;
            }

            var id = lines[0].Replace("ID: ", "").Trim();
            var product = products.FirstOrDefault(p => p.URL.ToString() == id);

            if (product == null)
            {
                Log.Warning($"Received response for a product not in the original batch request. ID: {id}");
                continue;
            }

            var content = string.Join("\n", lines.Skip(1));
            var parts = content.Split(["###---###"], StringSplitOptions.None);

            if (parts.Length != 3)
            {
                Log.Error($"Failed to parse response for product ID {id}. Expected 3 parts, got {parts.Length}.");
                continue;
            }

            var shortDesc = parts[0].Trim();
            var seoSentence = parts[1].Trim();
            var keywords = parts[2].Trim();

            // Update the product with the generated content
            product.ShortDescription = shortDesc;
            product.SEO = new(product.Title, seoSentence, keywords);
        }
    }
}