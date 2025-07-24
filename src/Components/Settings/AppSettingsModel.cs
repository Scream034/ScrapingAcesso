namespace ScraperAcesso.Components.Settings;

/// <summary>
/// Модель для хранения настроек приложения в файле.
/// Все поля, содержащие чувствительные данные, хранятся в зашифрованном виде.
/// </summary>
public class AppSettingsModel
{
    public string? EncryptedAuthUrl { get; set; }
    public string? EncryptedUsername { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? EncryptedGeminiApiKey { get; set; }
}