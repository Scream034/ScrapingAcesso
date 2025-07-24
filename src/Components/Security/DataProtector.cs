#pragma warning disable CA1416 // Проверка совместимости платформы

namespace ScraperAcesso.Components.Security;

using System.Security.Cryptography;
using System.Text;

using ScraperAcesso.Components.Log;

/// <summary>
/// Предоставляет методы для шифрования и дешифрования строк с использованием
/// Windows Data Protection API (DPAPI). Данные, зашифрованные одним пользователем,
/// не могут быть расшифрованы другим.
/// </summary>
public static class DataProtector
{
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("AcessoScraper.v1.Entropy");

    public static string Encrypt(in string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var data = Encoding.UTF8.GetBytes(plainText);
        var encryptedData = ProtectedData.Protect(data, s_entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedData);
    }

    public static string Decrypt(in string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return string.Empty;

        try
        {
            var encryptedData = Convert.FromBase64String(encryptedText);
            var decryptedData = ProtectedData.Unprotect(encryptedData, s_entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedData);
        }
        catch (CryptographicException ex)
        {
            Log.Error($"Failed to decrypt data: {ex.Message}");
            return string.Empty;
        }
    }
}

#pragma warning restore CA1416 // Проверка совместимости платформы