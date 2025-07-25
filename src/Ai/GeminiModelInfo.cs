namespace ScraperAcesso.Ai;

/// <summary>
/// Represents the configuration and rate limits for a specific Gemini model.
/// </summary>
/// <param name="ApiName">The official name of the model for the API (e.g., "gemini-1.5-flash-latest").</param>
/// <param name="Rpm">Requests Per Minute limit.</param>
/// <param name="Tpm">Tokens Per Minute limit (пока не используется, но задел на будущее).</param>
/// <param name="Rpd">Requests Per Day limit.</param>
public record GeminiModelInfo(string ApiName, int Rpm, int Tpm, int Rpd);

// Вспомогательный класс для десериализации файла конфигурации.
public class GeminiModelConfig
{
    public List<GeminiModelInfo> Models { get; set; } = [];
}