namespace ScraperAcesso.Utils;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;

/// <summary>
/// Утилита для высокопроизводительного чтения размеров изображений напрямую из байтов файла.
/// Использует Span<T> и stackalloc для минимизации нагрузки на сборщик мусора.
/// Поддерживает форматы: JPG, JPEG, PNG.
/// </summary>
public static class ImageUtils
{
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