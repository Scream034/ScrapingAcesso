namespace ScraperAcesso.Utils;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;

using ScraperAcesso.Components.Log;

using SkiaSharp;

/// <summary>
/// Утилита для высокопроизводительного чтения размеров изображений напрямую из байтов файла.
/// Использует Span<T> и stackalloc для минимизации нагрузки на сборщик мусора.
/// Поддерживает форматы: JPG, JPEG, PNG.
/// </summary>
public static class ImageUtils
{
    private const int JpegQuality = 85;
    private const int MaxCompressionCycles = 10; 

    /// <summary>
    /// Итеративно сжимает изображение, пока его размер не станет меньше maxFileSize.
    /// На каждой итерации разрешение уменьшается вдвое.
    /// Сохраняет сжатую версию поверх оригинального файла.
    /// </summary>
    /// <param name="filePath">Путь к файлу изображения.</param>
    /// <param name="maxFileSize">Максимально допустимый размер файла в байтах.</param>
    /// <returns>True, если сжатие было выполнено. False, если сжатие не требовалось.</returns>
    public static bool CompressImageIfNeeded(in string filePath, in uint maxFileSize = 5 * 1024 * 1024)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length <= maxFileSize)
        {
            return false;
        }

        Log.Print($"Image '{Path.GetFileName(filePath)}' is too large ({fileInfo.Length / 1024:N0} KB). Starting iterative compression...");

        try
        {
            byte[] imageBytes = File.ReadAllBytes(filePath);
            int cycles = 0;

            // Цикл будет работать, пока размер файла больше лимита и мы не превысили количество циклов
            while (imageBytes.Length > maxFileSize && cycles < MaxCompressionCycles)
            {
                cycles++;
                Log.Print($"  [Cycle {cycles}] Current size: {imageBytes.Length / 1024:N0} KB. Resizing...");

                using var originalBitmap = SKBitmap.Decode(imageBytes);
                if (originalBitmap == null)
                {
                    Log.Warning("  Could not decode image bytes during a cycle. Stopping compression.");
                    return false;
                }

                // Уменьшаем разрешение вдвое
                int newWidth = originalBitmap.Width / 2;
                int newHeight = originalBitmap.Height / 2;

                // Если разрешение стало слишком маленьким, прекращаем
                if (newWidth < 1 || newHeight < 1)
                {
                    Log.Warning("  Image dimensions became too small. Stopping compression.");
                    break;
                }

                using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKSamplingOptions.Default);
                if (resizedBitmap == null)
                {
                    Log.Warning("  Could not resize bitmap. Stopping compression.");
                    return false;
                }

                using var image = SKImage.FromBitmap(resizedBitmap);
                var format = Path.GetExtension(filePath).ToLowerInvariant() == ".png"
                    ? SKEncodedImageFormat.Png
                    : SKEncodedImageFormat.Jpeg;

                using var data = image.Encode(format, JpegQuality);
                imageBytes = data.ToArray(); // Обновляем байты для следующей итерации
            }

            // После цикла проверяем, нужно ли перезаписывать файл
            if (cycles > 0)
            {
                // Если мы вышли из цикла потому что размер стал приемлемым
                if (imageBytes.Length <= maxFileSize)
                {
                    File.WriteAllBytes(filePath, imageBytes);
                    Log.Print($"Successfully compressed '{Path.GetFileName(filePath)}'. Final size: {imageBytes.Length / 1024:N0} KB after {cycles} cycle(s).");
                    return true;
                }
                else
                {
                    // Если вышли по количеству циклов, но размер все еще большой
                    Log.Warning($"Could not compress '{Path.GetFileName(filePath)}' to the target size after {MaxCompressionCycles} cycles. The file was not modified.");
                    return false;
                }
            }

            return false; // Сжатие не потребовалось или не удалось
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred during iterative compression of '{filePath}'. Reason: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Получает размеры изображения (ширину и высоту) из файла, читая только заголовок.
    /// </summary>
    /// <param name="filePath">Путь к файлу изображения.</param>
    /// <returns>Vector2, где X - ширина, Y - высота.</returns>
    /// <exception cref="FileNotFoundException">Если файл не найден.</exception>
    /// <exception cref="NotSupportedException">Если формат файла не поддерживается или файл поврежден.</exception>
    public static Vector2 GetImageDimensions(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> header = stackalloc byte[24]; // 24 байта достаточно для определения и PNG, и JPG
        int bytesRead = stream.Read(header);

        if (bytesRead < 8)
            throw new NotSupportedException("Format not supported or file is corrupted.");

        // Проверяем сигнатуру JPEG (FF D8)
        if (header[0] == 0xFF && header[1] == 0xD8)
        {
            stream.Position = 0;
            return GetJpgDimensions(stream);
        }

        // Проверяем сигнатуру PNG (89 50 4E 47 0D 0A 1A 0A)
        if (header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return GetPngDimensionsFromHeader(header);
        }

        throw new NotSupportedException("Format not supported or file is corrupted.");
    }

    /// <summary>
    /// Проверяет, находится ли указанное разрешение в заданных минимальных и максимальных границах.
    /// </summary>
    /// <param name="resolution">Разрешение для проверки (X - ширина, Y - высота).</param>
    /// <param name="minResolution">Минимальное допустимое разрешение.</param>
    /// <param name="maxResolution">Максимальное допустимое разрешение.</param>
    /// <returns>true, если разрешение входит в диапазон, иначе false.</returns>
    public static bool IsResolutionInRange(Vector2 resolution, Vector2 minResolution, Vector2 maxResolution)
    {
        return resolution.X >= minResolution.X &&
               resolution.Y >= minResolution.Y &&
               resolution.X <= maxResolution.X &&
               resolution.Y <= maxResolution.Y;
    }

    private static Vector2 GetPngDimensionsFromHeader(ReadOnlySpan<byte> header)
    {
        // Ширина и высота в PNG (32-битные целые) находятся по смещению 16 и 20 байт от начала файла.
        if (header.Length < 24)
            throw new NotSupportedException("Format is not PNG (invalid PNG signature).");

        int width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));

        return new Vector2(width, height);
    }

    private static Vector2 GetJpgDimensions(FileStream stream)
    {
        stream.Seek(2, SeekOrigin.Begin); // Пропускаем маркер SOI (FF D8)

        Span<byte> buffer = stackalloc byte[2];

        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(buffer[..1]); // Читаем один байт

            if (buffer[0] != 0xFF) continue; // Ищем начало маркера

            stream.ReadExactly(buffer[..1]); // Читаем тип маркера
            byte markerType = buffer[0];

            if (markerType == 0xFF) continue; // Пропускаем заполнители

            // 0xC0 (Baseline), 0xC1 (Extended sequential), 0xC2 (Progressive) - все содержат размеры
            if (markerType >= 0xC0 && markerType <= 0xC3)
            {
                stream.Seek(3, SeekOrigin.Current); // Пропускаем длину блока и точность

                stream.ReadExactly(buffer);
                ushort height = BinaryPrimitives.ReadUInt16BigEndian(buffer);

                stream.ReadExactly(buffer);
                ushort width = BinaryPrimitives.ReadUInt16BigEndian(buffer);

                return new Vector2(width, height);
            }

            // Если это не маркер размера, пропускаем весь сегмент
            stream.ReadExactly(buffer);
            ushort blockLength = BinaryPrimitives.ReadUInt16BigEndian(buffer);

            if (blockLength > 2)
            {
                stream.Seek(blockLength - 2, SeekOrigin.Current);
            }
        }

        throw new NotSupportedException("Format is not JPG or JPEG (invalid SOI marker).");
    }
}