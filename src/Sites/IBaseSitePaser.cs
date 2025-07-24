namespace ScraperAcesso.Sites;

using ScraperAcesso.Components;

public interface IBaseSiteParser : IDisposable
{
    /// <summary>
    /// Parses the site and returns the result.
    /// </summary>
    /// <param name="browser">The browser instance to use for parsing.</param>
    /// <returns>A task that represents the asynchronous operation, containing the parsed result.</returns>
    Task<bool> ParseAsync(ChromiumScraper browser);

    /// <summary>
    /// Gets the URL of the site.
    /// </summary>
    string URL { get; }
}