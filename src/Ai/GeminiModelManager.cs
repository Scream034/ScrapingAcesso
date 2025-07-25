namespace ScraperAcesso.Ai;

using ScraperAcesso.Components.Log;
using System.Collections.Concurrent;
using System.Text.Json;
using IOPath = System.IO.Path;

/// <summary>
/// Manages loading Gemini model configurations, tracking their usage state (RPM, RPD),
/// and providing the next available model based on rate limits.
/// This class is designed to be thread-safe and persists its state to disk.
/// </summary>
public class GeminiModelManager
{
    // Пути к файлам конфигурации и состояния.
    private readonly string _configFilePath = IOPath.Combine(Constants.Path.Folder.App, "gemini_models.json");
    private readonly string _stateFilePath = IOPath.Combine(Constants.Path.Folder.App, "gemini_state.json");

    // Потокобезопасный словарь для хранения временных меток всех запросов для каждой модели.
    // Ключ - ApiName модели, Значение - потокобезопасная очередь с метками времени UTC.
    private ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _requestTimestamps = new();

    // Список моделей, загруженный из файла конфигурации.
    private List<GeminiModelInfo> _models = [];

    // Объект для блокировки при записи в файл состояния, чтобы избежать гонки потоков.
    private readonly object _stateFileLock = new();

    /// <summary>
    /// Initializes the manager by loading model configurations and their persisted state.
    /// </summary>
    public void Initialize()
    {
        Log.Print("Initializing Gemini Model Manager...");
        LoadModels();
        LoadState();
        CleanupOldTimestamps(); // Очищаем устаревшие метки при старте
    }

    /// <summary>
    /// Finds the next available model that has not exceeded its RPM or RPD limits.
    /// The models are checked in the order they are defined in the config file (priority).
    /// </summary>
    /// <returns>A GeminiModelInfo object for a usable model, or null if all models are rate-limited.</returns>
    public GeminiModelInfo? GetNextAvailableModel()
    {
        var now = DateTime.UtcNow;
        var oneMinuteAgo = now.AddMinutes(-1);
        var oneDayAgo = now.AddDays(-1);

        // Проходим по моделям в порядке приоритета
        foreach (var model in _models)
        {
            if (!_requestTimestamps.TryGetValue(model.ApiName, out var timestamps))
            {
                // Если для модели нет ни одной метки, она точно доступна.
                return model;
            }

            // Считаем количество запросов за последнюю минуту и день.
            // ToList() создает копию, чтобы избежать проблем с многопоточностью при перечислении.
            var recentTimestamps = timestamps.ToList();
            var requestsInLastMinute = recentTimestamps.Count(t => t > oneMinuteAgo);
            var requestsInLastDay = recentTimestamps.Count(t => t > oneDayAgo);

            // Проверяем, не превышены ли лимиты RPM и RPD.
            if (requestsInLastMinute < model.Rpm && requestsInLastDay < model.Rpd)
            {
                // Эта модель доступна.
                return model;
            }
        }

        // Если мы дошли сюда, значит, все модели исчерпали свои лимиты.
        Log.Warning("All Gemini models are currently rate-limited.");
        return null;
    }

    /// <summary>
    /// Records a new request for a specific model by adding a UTC timestamp.
    /// This method is thread-safe.
    /// </summary>
    /// <param name="apiName">The ApiName of the model that was used.</param>
    public void RecordRequest(string apiName)
    {
        var now = DateTime.UtcNow;

        // Получаем или создаем очередь для данной модели.
        var timestamps = _requestTimestamps.GetOrAdd(apiName, _ => new ConcurrentQueue<DateTime>());
        timestamps.Enqueue(now);

        Log.Print($"Request recorded for model '{apiName}' at {now:HH:mm:ss}. RPM/RPD state updated.");

        // Сохраняем состояние асинхронно в фоновом потоке, чтобы не блокировать основной.
        Task.Run(SaveState);
    }

    /// <summary>
    /// Loads the list of models and their limits from the JSON config file.
    /// If the file doesn't exist, it creates a default one.
    /// </summary>
    private void LoadModels()
    {
        if (!File.Exists(_configFilePath))
        {
            Log.Warning($"Config file '{_configFilePath}' not found. Creating a default one.");
            CreateDefaultConfigFile();
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<GeminiModelConfig>(json);
            _models = config?.Models ?? [];
            if (_models.Count == 0)
            {
                Log.Error("No models found in the config file. AI features will not work.");
            }
            else
            {
                Log.Print($"Loaded {_models.Count} models from config.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load or parse model config file '{_configFilePath}'. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the request timestamp history from the state file.
    /// This allows rate limit tracking to persist across application restarts.
    /// </summary>
    private void LoadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            Log.Print("State file not found. Starting with a clean state.");
            return;
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            // Десериализуем во временный словарь
            var loadedDict = JsonSerializer.Deserialize<Dictionary<string, List<DateTime>>>(json);

            if (loadedDict != null)
            {
                // Преобразуем в потокобезопасный ConcurrentDictionary
                var tempConcurrentDict = new ConcurrentDictionary<string, ConcurrentQueue<DateTime>>();
                foreach (var pair in loadedDict)
                {
                    tempConcurrentDict.TryAdd(pair.Key, new ConcurrentQueue<DateTime>(pair.Value));
                }
                _requestTimestamps = tempConcurrentDict;
                Log.Print($"Loaded request state for {_requestTimestamps.Count} models.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load state file '{_stateFilePath}'. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the current request timestamp history to the state file.
    /// This method is thread-safe.
    /// </summary>
    public void SaveState()
    {
        // Блокируем доступ к файлу, чтобы предотвратить одновременную запись из разных потоков.
        lock (_stateFileLock)
        {
            try
            {
                // Преобразуем ConcurrentDictionary в обычный словарь для сериализации
                var dictToSave = _requestTimestamps.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(dictToSave, options);
                File.WriteAllText(_stateFilePath, json);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save Gemini state file. Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Removes timestamps that are older than the maximum tracking window (1 day) to prevent the state file from growing indefinitely.
    /// </summary>
    private void CleanupOldTimestamps()
    {
        var oneDayAgo = DateTime.UtcNow.AddDays(-1);
        int removedCount = 0;

        foreach (var key in _requestTimestamps.Keys)
        {
            if (_requestTimestamps.TryGetValue(key, out var queue))
            {
                var initialCount = queue.Count;
                // Создаем новую очередь только с "актуальными" метками
                var newQueue = new ConcurrentQueue<DateTime>(queue.Where(t => t >= oneDayAgo));
                _requestTimestamps[key] = newQueue;
                removedCount += initialCount - newQueue.Count;
            }
        }

        if (removedCount > 0)
        {
            Log.Print($"Cleanup: Removed {removedCount} stale timestamps older than 24 hours.");
            SaveState(); // Сохраняем очищенное состояние
        }
    }

    /// <summary>
    /// Creates a default gemini_models.json file for the user.
    /// </summary>
    private void CreateDefaultConfigFile()
    {
        var defaultConfig = new GeminiModelConfig
        {
            Models =
            [
                new("gemini-2.0-flash-lite", 30, 1_000_000, 200), // Starting with the most expensive model
                new("gemini-2.5-flash-lite", 15, 1_000_000, 1000),
                new("gemini-2.5-flash", 10, 250_000, 250),
                new("gemini-2.0-flash", 15, 1_000_000, 200),
                new("gemini-1.5-flash-latest", 15, 1_000_000, 50),
            ]
        };

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not create default config file. Error: {ex.Message}");
        }
    }
}