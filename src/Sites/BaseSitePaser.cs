namespace ScraperAcesso.Sites;

using Microsoft.Playwright;

using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;

public abstract class BaseSiteParser : IDisposable
{
    /// <summary>
    /// Parses the site and returns the result.
    /// </summary>
    /// <param name="browser">The browser instance to use for parsing.</param>
    /// <returns>A task that represents the asynchronous operation, containing the parsed result.</returns>
    public abstract Task<bool> ParseAsync(ChromiumScraper browser);

    /// <summary>
    /// Gets the URL of the site.
    /// </summary>
    public abstract string URL { get; }

    /// <summary>
    /// Gets the page associated with the site parser.
    /// </summary>
    public abstract IPage? Page { get; protected set; }

    /// <summary>
    /// Closes the site parser and releases any resources.
    /// </summary>
    public virtual async Task CloseAsync()
    {
        if (Page != null)
        {
            string pageTitle = await Page.TitleAsync();
            await Page.CloseAsync();
            Page = null;
            Log.Print($"Page: '{pageTitle}', closed.");
        }
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}