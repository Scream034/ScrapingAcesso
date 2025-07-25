namespace ScraperAcesso.Components.Settings;

public sealed class AppSettingsModel
{
    public string? EncryptedAuthUrl { get; set; }
    public string? EncryptedUsername { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? EncryptedGeminiApiKey { get; set; }
    public bool AutoGenerateSeoOnParse { get; set; } = false;
}