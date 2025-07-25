namespace ScraperAcesso.Components.Settings;

using System.Text.Json;
using ScraperAcesso.Components.Security;
using ScraperAcesso.Components.Log;

/// <summary>
/// Manages application settings, including loading, saving, and encrypting sensitive data.
/// Uses a JSON file for persistent storage.
/// </summary>
public sealed class SettingsManager
{
    private readonly string _settingsFilePath;
    private AppSettingsModel _settings;

    public SettingsManager()
    {
        _settingsFilePath = Constants.Path.File.Settings;
        _settings = new AppSettingsModel();
    }

    public void Load()
    {
        Log.Print($"Loading settings from file: {_settingsFilePath}");
        if (!File.Exists(_settingsFilePath))
        {
            Log.Warning("Settings file not found. Default values will be used.");
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            _settings = JsonSerializer.Deserialize<AppSettingsModel>(json) ?? new AppSettingsModel();
            Log.Print("Settings loaded successfully.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error reading settings file: {ex.Message}");
            _settings = new AppSettingsModel();
        }
    }

    public void Save()
    {
        Log.Print("Saving settings...");
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsFilePath, json);
            Log.Print($"Settings saved successfully to {_settingsFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Error saving settings file: {ex.Message}");
        }
    }

    #region Decryption Methods

    /// <summary>
    /// Decrypts and returns the last saved authorization URL.
    /// </summary>
    public string GetDecryptedAuthUrl() => DataProtector.Decrypt(_settings.EncryptedAuthUrl ?? string.Empty);

    /// <summary>
    /// Decrypts and returns the last saved username.
    /// </summary>
    public string GetDecryptedUsername() => DataProtector.Decrypt(_settings.EncryptedUsername ?? string.Empty);

    /// <summary>
    /// Decrypts and returns the last saved password.
    /// </summary>
    public string GetDecryptedPassword() => DataProtector.Decrypt(_settings.EncryptedPassword ?? string.Empty);

    /// <summary>
    /// Decrypts and returns the last saved Gemini API key.
    /// </summary>
    public string GetDecryptedGeminiApiKey() => DataProtector.Decrypt(_settings.EncryptedGeminiApiKey ?? string.Empty);

    #endregion

    public bool GetAutoSeoEnabled() => _settings.AutoGenerateSeoOnParse;
    public void SetAutoSeoEnabled(bool isEnabled)
    {
        if (_settings.AutoGenerateSeoOnParse == isEnabled) return;
        _settings.AutoGenerateSeoOnParse = isEnabled;
        Save();
        Log.Print($"Setting 'AutoGenerateSeoOnParse' updated to: {isEnabled}");
    }

    /// <summary>
    /// Decrypts and returns the last saved Gemini API key.
    /// </summary>
    public void UpdateAndSaveGeminiApiKey(string apiKey)
    {
        _settings.EncryptedGeminiApiKey = DataProtector.Encrypt(apiKey);
        Save();
    }

    /// <summary>
    /// Updates the authorization data, encrypts it, and saves the settings.
    /// </summary>
    public void UpdateAndSaveAuthInfo(string url, string username, string password)
    {
        _settings.EncryptedAuthUrl = DataProtector.Encrypt(url);
        _settings.EncryptedUsername = DataProtector.Encrypt(username);
        _settings.EncryptedPassword = DataProtector.Encrypt(password);
        Save();
    }
}