namespace ScraperAcesso.Ai;

using ScraperAcesso.Components.Log;
using ScraperAcesso.Product;
using System.Collections.Concurrent;
using System.Threading.Tasks;

public static class GeminiBatchProcessor
{
    private static readonly ConcurrentQueue<BaseProduct> s_productQueue = new();
    private static readonly CancellationTokenSource s_cts = new();
    private static Task? s_processingTask;

    // Настройки батчинга
    private const int BatchSize = 3; // Сколько товаров в одном запросе
    private static readonly TimeSpan s_dispatchInterval = TimeSpan.FromSeconds(8); // Как часто проверять очередь
    private static TaskCompletionSource<bool> s_idleTcs = new();

    static GeminiBatchProcessor()
    {
        s_idleTcs.SetResult(true);
    }

    private static void Initialize()
    {
        Log.Print("Initializing Gemini Batch Processor...");
        s_processingTask = Task.Run(() => ProcessingLoopAsync(s_cts.Token));
        Log.Print("Initialization complete.");
    }

    public static async Task ShutdownAsync()
    {
        Log.Print("Shutting down Gemini Batch Processor...");
        s_cts.Cancel();
        if (s_processingTask != null)
        {
            await s_processingTask;
        }
        // Обработать оставшиеся в очереди элементы перед выходом
        await ProcessQueueAsync();
        Log.Print("Gemini Batch Processor shut down.");
    }

    /// <summary>
    /// Adds a product to the processing queue.
    /// </summary>
    public static async Task EnqueueAsync(BaseProduct product)
    {
        if (string.IsNullOrWhiteSpace(product.Description))
        {
            Log.Warning($"Product '{product.Title}' has no description. Skipping SEO generation.");
            return;
        }
        else if (s_processingTask == null)
        {
            Log.Print("Batch processing background task not started. Starting now...");
            Initialize();
            await Task.Delay(s_dispatchInterval.Add(s_dispatchInterval / 4)); // For init
            Log.Print("Batch processing background task started.");
        }

        s_productQueue.Enqueue(product);

        // Если добавили работу, а система была в "покое", создаем новую задачу для ожидания.
        if (s_idleTcs.Task.IsCompleted)
        {
            s_idleTcs = new TaskCompletionSource<bool>();
        }

        Log.Print($"Product '{product.Title}' enqueued for SEO generation.");
    }

    public static Task WaitForIdleAsync()
    {
        return s_idleTcs.Task;
    }

    private static async Task ProcessingLoopAsync(CancellationToken token)
    {
        Log.Print("Batch processing background task started.");
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(s_dispatchInterval, token);
            await ProcessQueueAsync();
        }
        Log.Print("Batch processing background task stopped.");
    }

    private static async Task ProcessQueueAsync()
    {
        if (s_productQueue.IsEmpty)
        {
            // If the queue is empty, set the idle TCS and return.
            s_idleTcs.TrySetResult(true);
            return;
        }

        Log.Print($"{s_productQueue.Count} products in SEO queue. Processing in batches of {BatchSize}.");

        while (!s_productQueue.IsEmpty)
        {
            var productsBatch = new List<BaseProduct>();
            for (int i = 0; i < BatchSize && !s_productQueue.IsEmpty; i++)
            {
                if (s_productQueue.TryDequeue(out var product))
                {
                    productsBatch.Add(product);
                }
            }

            if (productsBatch.Count > 0)
            {
                Log.Print($"Processing batch of {productsBatch.Count} products...");
                bool success = await GeminiService.GenerateContentForBatchAsync(productsBatch);
                if (success)
                {
                    Log.Print("Batch processed successfully. Saving results...");
                    foreach (var p in productsBatch.Where(prod => prod.SEO != null))
                    {
                        // Сохраняем каждый продукт, у которого появились SEO-данные
                        _ = p.SaveAsync();
                    }
                }
                else
                {
                    Log.Error("Failed to process a batch. Products might be re-queued on next run if not saved.");
                    // Можно реализовать логику повторной постановки в очередь
                }
            }
        }
    }
}