namespace ScraperAcesso.Utils;

using System.Security.Cryptography;
using ScraperAcesso.Components.Log;

/// <summary>
/// Utility class for downloading images from a list of URLs and saving them to a specified folder.
/// </summary>
public static class ImageDownloader
{
    private static readonly HttpClient s_httpClient = new();

    /// <summary>
    ///  Asynchronously downloads images from the provided URLs and saves them to the specified folder.
    ///  Returns a list of paths to the saved images.
    /// </summary>
    /// <param name="imageUrls">Коллекция URL-адресов изображений для скачивания.</param>
    /// <param name="destinationFolder">Папка, в которую будут сохранены изображения.</param>
    /// <returns>Список путей к локально сохраненным файлам.</returns>
    public static async Task<List<string>> DownloadAndSaveAsync(IEnumerable<string> imageUrls, string destinationFolder)
    {
        if (imageUrls == null || !imageUrls.Any())
        {
            return []; // Возвращаем пустой список, если нет URL для скачивания
        }

        // Убедимся, что папка для сохранения существует
        Directory.CreateDirectory(destinationFolder);

        // Создаем список задач на скачивание для каждого URL
        var downloadTasks = imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url)) // Отфильтровываем пустые URL
            .Select(url => DownloadSingleImageAsync(url, destinationFolder));

        // Ожидаем завершения всех задач параллельно
        var savedFilePaths = await Task.WhenAll(downloadTasks);

        // Отфильтровываем неудачные загрузки (которые вернули null) и возвращаем результат
        var successfulPaths = savedFilePaths.Where(path => path != null).ToList();

        Log.Print($"Downloaded {successfulPaths.Count} of {imageUrls.Count()} images.");

        return successfulPaths!;
    }

    /// <summary>
    /// Логика скачивания и сохранения одного изображения.
    /// </summary>
    private static async Task<string?> DownloadSingleImageAsync(string url, string destinationFolder)
    {
        try
        {
            // Асинхронно получаем изображение в виде массива байт
            var imageBytes = await s_httpClient.GetByteArrayAsync(url);
            if (imageBytes.Length == 0)
            {
                Log.Warning($"Received empty content from URL: {url}");
                return null;
            }

            // Вычисляем хэш для имени файла
            var hashBytes = SHA256.HashData(imageBytes);
            // Преобразуем хэш в строку вида "e3b0c44298fc1c14..."
            var hashString = Convert.ToHexStringLower(hashBytes);

            // Определяем расширение файла из URL, с фолбэком на .jpg
            var uri = new Uri(url);
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrEmpty(extension) || extension.Length > 5) // Простая проверка на адекватность расширения
            {
                extension = ".jpg";
            }

            var finalFileName = $"{hashString}{extension}";
            var finalFilePath = Path.Combine(destinationFolder, finalFileName);

            // Проверяем, существует ли файл, чтобы избежать повторной записи на диск
            if (!File.Exists(finalFilePath))
            {
                await File.WriteAllBytesAsync(finalFilePath, imageBytes);
            }

            return finalFilePath; // Возвращаем полный локальный путь к файлу
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to download image from {url}. Error: {ex.Message}");
            return null; // В случае ошибки возвращаем null
        }
    }
}