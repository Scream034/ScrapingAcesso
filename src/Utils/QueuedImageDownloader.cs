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
    private static readonly string s_queueFilePath = IOPath.Combine(Constants.Path.Folder.App, "download_queue.json");
    // Используем ConcurrentQueue, так как она идеально подходит для модели "производитель-потребитель".
    private static readonly ConcurrentQueue<DownloadJob> s_jobQueue = new();
    private static readonly HttpClient s_httpClient = new();

    private static CancellationTokenSource? s_cts;
    private static Task? s_processingTask;
    // TaskCompletionSource для механизма ожидания "покоя" (когда все задачи выполнены).
    private static TaskCompletionSource<bool> s_idleTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    static QueuedImageDownloader()
    {
        // Изначально система в "покое".
        s_idleTcs.SetResult(true);
    }

    /// <summary>
    /// Initializes the downloader, loads pending jobs, and starts the background processing.
    /// </summary>
    /// <param name="maxConcurrentDownloads">The maximum number of images to download simultaneously.</param>
    public static void Initialize(int maxConcurrentDownloads = 8) // Увеличил значение по умолчанию
    {
        Log.Print($"Initializing QueuedImageDownloader with {maxConcurrentDownloads} concurrent downloads.");
        s_cts = new CancellationTokenSource();

        LoadQueueFromDisk();

        // Запускаем главный цикл обработки в фоновом потоке.
        s_processingTask = Task.Run(() => ProcessQueueInParallelAsync(maxConcurrentDownloads, s_cts.Token));
    }

    /// <summary>
    /// Signals the downloader to stop processing and saves the current queue state.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (s_cts == null || s_processingTask == null) return;

        Log.Print("Shutting down QueuedImageDownloader...");

        // Если есть активные задачи, дожидаемся их завершения.
        if (!s_idleTcs.Task.IsCompleted)
        {
            Log.Print("Waiting for active downloads to complete before shutdown...");
            await WaitForIdleAsync();
        }

        s_cts.Cancel();
        try
        {
            await s_processingTask;
        }
        catch (OperationCanceledException)
        {
            // Это ожидаемое исключение при отмене.
        }

        SaveQueueToDisk();
        Log.Print("QueuedImageDownloader shut down successfully.");
    }

    /// <summary>
    /// Adds a new image to the download queue and returns the expected local file path.
    /// </summary>
    public static string Enqueue(string imageUrl, string destinationFolder)
    {
        var finalFilePath = GetDeterministicFilePath(imageUrl, destinationFolder);
        var job = new DownloadJob(imageUrl, finalFilePath);

        s_jobQueue.Enqueue(job);

        // Если мы добавили новую работу, а система была в "покое",
        // нужно "сбросить" событие ожидания, создав новое.
        if (s_idleTcs.Task.IsCompleted)
        {
            s_idleTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Log.Print($"Image enqueued: {Path.GetFileName(finalFilePath)}");
        return finalFilePath;
    }

    /// <summary>
    /// Returns a task that completes when the download queue is empty and all active downloads are finished.
    /// </summary>
    public static Task WaitForIdleAsync()
    {
        return s_idleTcs.Task;
    }

    /// <summary>
    /// The main background processing loop. It continuously pulls jobs from the queue
    /// and processes them in parallel, up to the specified limit.
    /// </summary>
    private static async Task ProcessQueueInParallelAsync(int maxParallelism, CancellationToken token)
    {
        Log.Print("Image download background task started.");
        var runningTasks = new List<Task>();

        while (!token.IsCancellationRequested)
        {
            // Очищаем список завершившихся задач
            runningTasks.RemoveAll(t => t.IsCompleted);

            // Если есть свободные "слоты" для загрузки и в очереди есть задачи
            while (runningTasks.Count < maxParallelism && s_jobQueue.TryDequeue(out var job))
            {
                // Запускаем задачу и добавляем ее в список активных
                runningTasks.Add(DownloadAndProcessImageAsync(job, token));
            }

            // Если в данный момент нет активных задач и очередь пуста,
            // значит, мы достигли состояния "покоя".
            if (runningTasks.Count == 0 && s_jobQueue.IsEmpty)
            {
                s_idleTcs.TrySetResult(true);
                // Ждем немного, прежде чем снова проверять очередь.
                await Task.Delay(500, token);
            }
            else
            {
                // Ждем, пока любая из активных задач не завершится,
                // чтобы освободить "слот" для следующей.
                if (runningTasks.Count > 0)
                {
                    await Task.WhenAny(runningTasks);
                }
            }
        }

        // Дожидаемся всех оставшихся задач после получения сигнала отмены
        if (runningTasks.Count > 0)
        {
            await Task.WhenAll(runningTasks);
        }

        Log.Print("Image download background task stopped.");
    }

    /// <summary>
    /// Handles the entire lifecycle of a single download job: download, save, and compress.
    /// </summary>
    private static async Task DownloadAndProcessImageAsync(DownloadJob job, CancellationToken token)
    {
        try
        {
            if (File.Exists(job.DestinationPath))
            {
                Log.Print($"Image already exists, skipping: {Path.GetFileName(job.DestinationPath)}");
                return;
            }

            // Скачивание
            var imageBytes = await s_httpClient.GetByteArrayAsync(job.ImageUrl, token);
            if (imageBytes.Length == 0)
            {
                Log.Warning($"Received empty content from URL: {job.ImageUrl}");
                return;
            }

            // Сохранение
            Directory.CreateDirectory(IOPath.GetDirectoryName(job.DestinationPath)!);
            await File.WriteAllBytesAsync(job.DestinationPath, imageBytes, token);
            Log.Print($"Downloaded: {job.DestinationPath}");

            // Сжатие
            ImageUtils.CompressImageIfNeeded(job.DestinationPath);
        }
        catch (OperationCanceledException)
        {
            Log.Warning($"Download canceled for: {job.ImageUrl}");
            // Возвращаем в очередь при отмене, чтобы задача не потерялась
            s_jobQueue.Enqueue(job);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to process job for {job.ImageUrl}. Error: {ex.Message}. Re-enqueuing.");
            // Возвращаем в очередь при ошибке для повторной попытки
            s_jobQueue.Enqueue(job);
        }
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
                    s_jobQueue.Enqueue(job);
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
            var jobs = s_jobQueue.ToList();
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