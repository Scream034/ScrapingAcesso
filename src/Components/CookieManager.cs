using System.Text.Json;
using Microsoft.Playwright;

namespace ScraperAcesso.Components;

public class CookieManager
{
    public static async Task SaveAsync(string filename, IBrowserContext context)
    {
        IReadOnlyList<BrowserContextCookiesResult> cookies = await context.CookiesAsync();
        string? json = JsonSerializer.Serialize(cookies);
        await File.WriteAllTextAsync(filename, json);
    }

    public static async Task LoadAsync(string filename, IBrowserContext context)
    {
        if (!File.Exists(filename))
        {
            throw new FileNotFoundException("Cookie file not found", filename);
        }

        string? json = await File.ReadAllTextAsync(filename);
        Cookie[]? cookies = JsonSerializer.Deserialize<Cookie[]>(json);
        if (cookies == null)
        {
            throw new Exception("Cookies in context was not found");
        }

        await context.AddCookiesAsync(cookies);
    }
}