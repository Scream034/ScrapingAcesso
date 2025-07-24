namespace ScraperAcesso.Components.Log;

using Internal;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

public static class Log
{
    /// <summary>
    /// Время ожидания между проверкой очереди сообщений
    /// </summary>
    public const int WaitTime = 150;

    private static bool _isInitialized = false;
    private static readonly ConcurrentQueue<LogMessage> _logQueue = new(); // **Потокобезопасная очередь сообщений**
    private static Thread? _logThread; // Поток для записи в лог
    private static bool _loggingEnabled = true; // Флаг для управления работой потока
    private static readonly AutoResetEvent _logThreadSignal = new(false); // Сигнал для потока, когда есть новые сообщения
    private static string _logPath = Constants.Path.File.Log;

    public static void Initialize()
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("Log already initialized");
        }

        if (File.Exists(Constants.Path.File.Log))
        {
            File.WriteAllText(Constants.Path.File.Log, string.Empty);
        }

        StartLogThread(); // Запускаем поток логирования при инициализации
        _isInitialized = true;
    }

    private static void StartLogThread()
    {
        if (_logThread == null)
        {
            _loggingEnabled = true; // Убеждаемся, что логирование включено
            _logThread = new Thread(LoggingThreadWorker);
            _logThread.IsBackground = true; // Делаем поток фоновым, чтобы он не мешал завершению приложения
            _logThread.Start();
        }
    }

    private static void StopLogging() // Метод для остановки потока логирования при завершении приложения (важно!)
    {
        _loggingEnabled = false; // Сигнализируем потоку, что нужно завершиться
        _logThreadSignal.Set(); // Даем сигнал потоку, чтобы он проснулся и проверил _loggingEnabled
        _logThread?.Join(); // Ждем завершения потока (необязательно, если поток фоновый)
        _logThread = null;
    }


    private static void ProcessMessagePrint(in LogMessage message)
    {
        switch (message.Level)
        {
            case LogLevel.Info:
                Console.WriteLine(message.Value);
                break;

            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(message.Value);
                Console.ResetColor();
                break;

            case LogLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(message.Value);
                Console.ResetColor();
                break;
        }
    }

    private static void ProcessMessageWrite(in LogMessage message)
    {
        using (StreamWriter sw = new StreamWriter(_logPath, true))
        {
            switch (message.Level)
            {
                case LogLevel.Info:
                    sw.WriteLine(message);
                    break;

                case LogLevel.Warning:
                    sw.WriteLine(message);
                    break;

                case LogLevel.Error:
                    sw.WriteLine(message);
                    break;
            }
        }
    }

    private static void LoggingThreadWorker()
    {
        while (_loggingEnabled)
        {
            if (_logQueue.TryDequeue(out var message))
            {
                if (message is null)
                {
                    // Если сообщение null, пропускаем его
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Log Thread Error: Dequeued null message");
                    Console.ResetColor();
                    continue;
                }
                ;

                try
                {
                    ProcessMessagePrint(message);
                    ProcessMessageWrite(message);
                }
                catch (Exception ex)
                {
                    // Обработка ошибок записи в файл (например, запись в консоль, если лог файл недоступен)
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Log Thread Error: Failed to write message: {ex.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                _logThreadSignal.WaitOne(WaitTime);
            }
        }
        // Поток завершается, когда _loggingEnabled становится false
    }


    public static void Print(params object[] messages)
    {
        QueueWrite(messages, 2);
    }

    public static void Warning(params object[] messages)
    {
        QueueWrite(messages, 2, LogLevel.Warning);
    }

    public static void Error(params object[] messages)
    {
        QueueWrite(messages, 2, LogLevel.Error);
    }

    public static void QueueWrite(in object[] messages, in ushort skipFrame = 1, in LogLevel level = LogLevel.Info)
    {
        LogMessage message = new(level, messages.Select(static m => m.ToString()).Aggregate(static (a, b) => $"{a} {b}") ?? string.Empty, new(skipFrame));

        _logQueue.Enqueue(message); // **Добавляем сообщение в очередь, а не пишем напрямую в файл**
        _logThreadSignal.Set(); // **Сигнализируем потоку логирования, что есть новые сообщения**
    }

    public static void Dispose()
    {
        if (_isInitialized) StopLogging();
        else throw new InvalidOperationException("Log is not initialized");
    }
}