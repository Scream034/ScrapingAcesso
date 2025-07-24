namespace ScraperAcesso.Ai;

using GenerativeAI;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product;
using System.Text;

/// <summary>
/// Статический сервис для взаимодействия с Google Gemini API для генерации контента.
/// </summary>
public static class GeminiService
{
    private static GenerativeModel? _generativeModel;

    /// <summary>
    /// Инициализирует сервис с предоставленным API ключом.
    /// </summary>
    /// <param name="apiKey">API ключ для Google Gemini.</param>
    /// <returns>True, если инициализация прошла успешно, иначе False.</returns>
    public static bool Initialize(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Error("Gemini API key is not provided.");
            return false;
        }

        try
        {
            _generativeModel = new GenerativeModel(model: "gemini-2.0-flash-lite", apiKey: apiKey);
            Log.Print("Gemini Service initialized successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to initialize Gemini Service. Check your API key or network connection. Error: {ex.Message}");
            _generativeModel = null;
            return false;
        }
    }

    /// <summary>
    /// Генерирует оптимизированный контент для продукта.
    /// </summary>
    /// <param name="product">Продукт, для которого генерируется контент.</param>
    public static async Task<bool> GenerateContentAsync(BaseProduct product)
    {
        if (_generativeModel == null)
        {
            Log.Error("Gemini Service is not initialized. Please configure the API key first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(product.Description))
        {
            Log.Warning($"Product '{product.Title}' has no description. Cannot generate SEO content.");
            return false;
        }

        var prompt = BuildPrompt(product);

        try
        {
            var rawResponse = await _generativeModel.GenerateContentAsync(prompt);
            var response = ParseResponse(rawResponse.Text);

            if (response != null)
            {
                product.ShortDescription = response.ShortDescription;
                product.SEO = new(product.Title, response.SeoSentence, response.Keywords);
                Log.Print($"SEO content generated for product '{product.Title}'.");
    
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred during Gemini API request for product '{product.Title}'. Error: {ex.Message}");
            return false;
        }

        return false;
    }

    /// <summary>
    /// Создает структурированный промпт для AI.
    /// </summary>
    private static string BuildPrompt(BaseProduct product)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ТЫ — ПРОФЕССИОНАЛЬНЫЙ SEO-КОПИРАЙТЕР И МАРКЕТОЛОГ. Твоя задача — создать качественный контент для карточки товара на основе предоставленных данных. Ответ должен быть на русском языке.");
        sb.AppendLine("\n--- ИСХОДНЫЕ ДАННЫЕ ---");
        sb.AppendLine($"Название товара: {product.Title}");

        if (product.Attributes.Any())
        {
            sb.AppendLine("Характеристики:");
            foreach (var attr in product.Attributes.Take(10))
            {
                sb.AppendLine($"- {attr.Name}: {attr.Value}");
            }
        }

        sb.AppendLine("\nПолное описание товара:");
        sb.AppendLine(product.Description);

        sb.AppendLine("\n--- ЗАДАНИЕ ---");
        sb.AppendLine("На основе данных выше, сгенерируй три блока текста:");
        sb.AppendLine($"1.  **Краткое описание:** Рерайт полного описания. Живой, продающий текст. Длина: до {BaseProduct.MaxShortDescriptionLength} символов.");
        sb.AppendLine($"2.  **SEO-предложение:** Одно ёмкое предложение для мета-описаний и превью. Упомяни главную выгоду или ключевую характеристику. Длина: до {SEOProductInfo.MaxSeoSentenceLength} символов.");
        sb.AppendLine($"3.  **Ключевые слова:** 5-7 самых релевантных ключевых слов через запятую. Длина: до {SEOProductInfo.MaxKeywordsLength} символов.");

        sb.AppendLine("\n--- ТРЕБОВАНИЯ К ФОРМАТУ ОТВЕТА ---");
        sb.AppendLine("Твой ответ должен содержать ТОЛЬКО три сгенерированных текстовых блока, разделенных между собой строкой '###---###'.");
        sb.AppendLine("НЕ включай в ответ заголовки 'Краткое описание:', 'SEO-предложение:', 'Ключевые слова:' или любой другой пояснительный текст.");
        sb.AppendLine("Пример правильной структуры ответа:");
        sb.AppendLine("[Сгенерированный текст краткого описания]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированное SEO-предложение]");
        sb.AppendLine("###---###");
        sb.AppendLine("[Сгенерированные ключевые слова]");

        return sb.ToString();
    }

    /// <summary>
    /// Парсит текстовый ответ от AI
    /// </summary>
    private static GeminiResponse? ParseResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            Log.Error("Gemini API returned an empty response.");
            return null;
        }

        var parts = responseText.Split(["###---###"], StringSplitOptions.None);
        if (parts.Length != 3)
        {
            Log.Error($"Failed to parse Gemini response. Expected 3 parts, but got {parts.Length}. Response: {responseText}");
            return null;
        }

        var shortDesc = parts[0].Trim();
        var seoSentence = parts[1].Trim();
        var keywords = parts[2].Trim();

        return new(shortDesc, seoSentence, keywords);
    }
}