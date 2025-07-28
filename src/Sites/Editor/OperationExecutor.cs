namespace ScraperAcesso.Sites.Editor;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product;

public sealed class OperationExecutor(in IPage page)
{
    private readonly IPage _page = page;
    private const int DefaultRetries = 5;
    private const int RetryDelayMs = 30000;
    private const int AdditionRetryDelayMs = 5000;

    public async Task ExecuteAsync(string operationName, Func<Task> operation, int maxRetries = DefaultRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var cts = new CancellationTokenSource();
            try
            {
                Log.Print($"---- Starting Operation: '{operationName}' (Attempt {attempt}/{maxRetries}) ----");

                var operationTask = operation();
                var errorWatcherTask = WatchForErrorsAsync(cts.Token);

                var completedTask = await Task.WhenAny(operationTask, errorWatcherTask);
                cts.Cancel();

                Log.Print($"--- Operation '{operationName}' completed successfully. ---");
                return;
            }
            catch (Exception ex)
            {
                if (ex is ExceedImagesLimitException) throw;

                Log.Warning($"Operation '{operationName}' failed on attempt {attempt}. Reason: {ex.GetType().Name}: {ex.Message}");

                if (ex is ServerErrorException)
                {
                    if (attempt < maxRetries)
                    {
                        int delayMs = RetryDelayMs + AdditionRetryDelayMs * attempt;
                        Log.Warning($"Server error detected. Waiting {delayMs / 1000}s before retrying...");
                        await Task.Delay(delayMs);
                        continue;
                    }
                    Log.Error($"Operation '{operationName}' failed after {maxRetries} attempts due to persistent server errors.");
                    throw;
                }

                if (attempt < maxRetries)
                {
                    var delayMs = AdditionRetryDelayMs * attempt / 2;
                    Log.Warning($"A non-server error occurred. Retrying after a short delay {delayMs / 1000}s...");
                    await Task.Delay(delayMs);
                    continue;
                }

                Log.Error($"Operation '{operationName}' failed definitively after {maxRetries} attempts.");
                throw;
            }
        }
    }

    private async Task WatchForErrorsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var errorModal = _page.Locator(EditorService.XPath.ServerErrorModal);
                if (await errorModal.IsVisibleAsync())
                {
                    string errorText = (await errorModal.InnerTextAsync()).ToLower().Trim();
                    Log.Warning($"Watcher detected a visible error modal! Text: {errorText}");

                    await errorModal.EvaluateAsync($"(e) => e.classList.add('{EditorService.CssClasses.Internal.ProcessedByScraper}')");

                    if (errorText.Contains("максимальное количество картинок")) throw new ExceedImagesLimitException();
                    if (errorText.Contains("internal server error")) throw new ServerErrorException(errorText);
                }
                await Task.Delay(500, token);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception) { throw; }
        }
    }

    public sealed class ExceedImagesLimitException() : Exception($"Exceeded the maximum number of images allowed ({BaseProduct.MaxImagesCount}).");
    public sealed class ServerErrorException(in string message) : Exception($"Server error modal appeared: {message}");
}