namespace ScraperAcesso.Utils;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScraperAcesso.Components.Log;

using IOPath = System.IO.Path;

/// <summary>
/// Represents a single image download job.
/// </summary>
/// <param name="ImageUrl">The public URL of the image to download.</param>
/// <param name="DestinationPath">The full local file path where the image will be saved.</param>
internal record DownloadJob(string ImageUrl, string DestinationPath);

/// <summary>
/// Manages a persistent, concurrent queue for downloading images in the background.
/// </summary>
public static class QueuedImageDownloader
{
    public const int LoopDelay = 500;
    public const int WaitingImagesDelay = 2200;

    private static readonly string s_queueFilePath = IOPath.Combine(Constants.Path.Folder.App, "download_queue.json");
    private static readonly ConcurrentDictionary<string, DownloadJob> s_jobQueue = new();
    private static readonly HttpClient s_httpClient = new();

    private static CancellationTokenSource? s_cts;
    private static SemaphoreSlim? s_semaphore;
    private static Task? s_processingTask;
    private static TaskCompletionSource<bool> s_idleTcs = new();
    private static uint s_activeDownloads = 0;

    static QueuedImageDownloader()
    {
        s_idleTcs.SetResult(true);
    }

    /// <summary>
    /// Initializes the downloader, loads pending jobs, and starts the background processing.
    /// </summary>
    /// <param name="maxConcurrentDownloads">The maximum number of images to download simultaneously.</param>
    public static void Initialize(int maxConcurrentDownloads = 5)
    {
        Log.Print($"Initializing QueuedImageDownloader with {maxConcurrentDownloads} concurrent downloads.");
        s_cts = new CancellationTokenSource();
        s_semaphore = new SemaphoreSlim(maxConcurrentDownloads, maxConcurrentDownloads);

        LoadQueueFromDisk();

        s_processingTask = Task.Run(() => ProcessQueueAsync(s_cts.Token));
    }

    /// <summary>
    /// Signals the downloader to stop processing and saves the current queue state.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (s_cts == null || s_processingTask == null) return;

        Log.Print("Shutting down QueuedImageDownloader...");
        s_cts.Cancel();
        await s_processingTask; // Wait for the processing loop to finish
        SaveQueueToDisk();
        Log.Print("QueuedImageDownloader shut down successfully.");
    }

    /// <summary>
    /// Adds a new image to the download queue and returns the expected local file path.
    /// </summary>
    /// <param name="imageUrl">The public URL of the image.</param>
    /// <param name="destinationFolder">The folder where the image should be saved.</param>
    /// <returns>The predicted full local path for the image file.</returns>
    public static string Enqueue(string imageUrl, string destinationFolder)
    {
        var finalFilePath = GetDeterministicFilePath(imageUrl, destinationFolder);
        var job = new DownloadJob(imageUrl, finalFilePath);

        if (s_jobQueue.TryAdd(imageUrl, job))
        {
            // Если мы добавили новую работу, а система была в "покое",
            // нужно создать новую "задачу-событие" для ожидания.
            if (s_idleTcs.Task.IsCompleted)
            {
                s_idleTcs = new TaskCompletionSource<bool>();
            }
            Log.Print($"Image enqueued: {imageUrl}");
        }

        return finalFilePath;
    }

    public static Task WaitForIdleAsync()
    {
        return s_idleTcs.Task;
    }

    private static async Task ProcessQueueAsync(CancellationToken token)
    {
        Log.Print("Image download background task started.");
        while (!token.IsCancellationRequested)
        {
            // Если очередь пуста, просто ждем.
            if (s_jobQueue.IsEmpty)
            {
                // Проверяем, не пора ли завершить "задачу-событие".
                // Это нужно на случай, если последняя задача завершилась, а очередь уже была пуста.
                if (Interlocked.CompareExchange(ref s_activeDownloads, 0, 0) == 0)
                {
                    s_idleTcs.TrySetResult(true);
                }

                await Task.Delay(WaitingImagesDelay, token);
                continue;
            }

            // Берем снимок задач, чтобы избежать вечного цикла, если задачи добавляются постоянно.
            var jobsToProcess = s_jobQueue.Keys.ToList();

            foreach (var imageUrl in jobsToProcess)
            {
                if (token.IsCancellationRequested) break;

                // Если задача все еще в очереди (не была обработана другим потоком)
                if (s_jobQueue.TryRemove(imageUrl, out var job))
                {
                    await s_semaphore!.WaitAsync(token);

                    // Увеличиваем счетчик активных задач
                    Interlocked.Increment(ref s_activeDownloads);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (!File.Exists(job.DestinationPath))
                            {
                                await DownloadAndSaveImageAsync(job);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Failed to process download job for {job.ImageUrl}. Error: {ex.Message}");
                            // Возвращаем в очередь при ошибке для повторной попытки
                            s_jobQueue.TryAdd(job.ImageUrl, job);
                        }
                        finally
                        {
                            s_semaphore.Release();
                            // Уменьшаем счетчик и проверяем, не стали ли мы "в покое"
                            if (Interlocked.Decrement(ref s_activeDownloads) == 0 && s_jobQueue.IsEmpty)
                            {
                                s_idleTcs.TrySetResult(true);
                            }
                        }
                    }, token);
                }
            }
            await Task.Delay(LoopDelay, token);
        }
        Log.Print("Image download background task stopped.");
    }

    private static async Task DownloadAndSaveImageAsync(DownloadJob job)
    {
        var imageBytes = await s_httpClient.GetByteArrayAsync(job.ImageUrl);
        if (imageBytes.Length == 0)
        {
            Log.Warning($"Received empty content from URL: {job.ImageUrl}");
            return;
        }

        Directory.CreateDirectory(IOPath.GetDirectoryName(job.DestinationPath)!);
        await File.WriteAllBytesAsync(job.DestinationPath, imageBytes);
        Log.Print($"Image downloaded and saved to {job.DestinationPath}");
    }

    /// <summary>
    /// Generates a deterministic file path based on the image URL.
    /// </summary>
    private static string GetDeterministicFilePath(string url, string destinationFolder)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var hashString = Convert.ToHexStringLower(hashBytes);

        var uri = new Uri(url);
        var extension = IOPath.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrEmpty(extension) || extension.Length > 5)
        {
            extension = ".jpg"; // default extension if none can be determined
        }

        return IOPath.Combine(destinationFolder, $"{hashString}{extension}");
    }

    private static void LoadQueueFromDisk()
    {
        if (!File.Exists(s_queueFilePath)) return;

        try
        {
            var json = File.ReadAllText(s_queueFilePath);
            var jobs = JsonSerializer.Deserialize<List<DownloadJob>>(json);
            if (jobs != null)
            {
                foreach (var job in jobs)
                {
                    s_jobQueue.TryAdd(job.ImageUrl, job);
                }
            }
            Log.Print($"Loaded {s_jobQueue.Count} pending image downloads from disk.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load download queue from disk: {ex.Message}");
        }
    }

    private static void SaveQueueToDisk()
    {
        try
        {
            var jobs = s_jobQueue.Values.ToList();
            var json = JsonSerializer.Serialize(jobs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(s_queueFilePath, json);
            Log.Print($"Saved {jobs.Count} pending image downloads to disk.");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save download queue to disk: {ex.Message}");
        }
    }
}