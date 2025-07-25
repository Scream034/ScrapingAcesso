using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ScraperAcesso.Components;

/// <summary>
/// Управляет экземпляром браузера Chromium и несколькими контекстами для эффективного и параллельного скрапинга.
/// Этот класс реализует IAsyncDisposable для корректного освобождения ресурсов.
/// </summary>
public sealed class ChromiumScraper : IAsyncDisposable
{
	// --- Поля и свойства ---

	private IPlaywright? _playwright;
	private IBrowser? _browser;

	// Потокобезопасный словарь для хранения и управления несколькими контекстами.
	// Ключ - уникальное имя контекста, Значение - объект IBrowserContext.
	private readonly ConcurrentDictionary<string, IBrowserContext> _contexts = new();

	/// <summary>
	/// Опции для запуска процесса браузера. Могут быть переопределены при создании экземпляра.
	/// </summary>
	public static BrowserTypeLaunchOptions DefaultDriverOptions { get; } = new()
	{
		Args = new[] { "--disable-web-security", "--disable-features=IsolateOrigins,site-per-process", "--incognito" },
		Headless = false,
		ChromiumSandbox = true,
		// Рекомендуется указывать Timeout, чтобы запуск не зависал навечно
		Timeout = 60_000 // 60 секунд
	};

	/// <summary>
	/// Опции по умолчанию для создания нового контекста браузера. Могут быть переопределены для каждого контекста.
	/// </summary>
	public static BrowserNewContextOptions DefaultContextOptions { get; } = new()
	{
		UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
		IgnoreHTTPSErrors = true,
		ViewportSize = new() { Width = 1280, Height = 720 },
		AcceptDownloads = true,
		ReducedMotion = ReducedMotion.Reduce,
		Geolocation = new() { Longitude = 60.6103F, Latitude = 56.8389F }, // Екатеринбург
		Permissions = new[] { "geolocation" },
		TimezoneId = "Asia/Yekaterinburg", // Урал
		Locale = "ru-RU",
		ColorScheme = ColorScheme.Dark,
		ExtraHTTPHeaders = new Dictionary<string, string>
		{
			{ "Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7" },
			{ "DNT", "1" } // Do Not Track
        }
	};

	// Приватный конструктор, чтобы заставить использовать фабричный метод CreateAsync
	private ChromiumScraper() { }

	// --- Фабричный метод для инициализации ---

	/// <summary>
	/// Асинхронно создает и инициализирует новый экземпляр ChromiumScraper.
	/// </summary>
	/// <param name="driverOptions">Необязательные параметры запуска браузера. Если null, используются DefaultDriverOptions.</param>
	/// <returns>Готовый к работе экземпляр ChromiumScraper.</returns>
	public static async Task<ChromiumScraper> CreateAsync(BrowserTypeLaunchOptions? driverOptions = null)
	{
		var scraper = new ChromiumScraper();
		await scraper.InitializeAsync(driverOptions ?? DefaultDriverOptions);
		return scraper;
	}

	private async Task InitializeAsync(BrowserTypeLaunchOptions driverOptions)
	{
		_playwright = await Playwright.CreateAsync();
		try
		{
			_browser = await _playwright.Chromium.LaunchAsync(driverOptions);
		}
		catch (PlaywrightException ex) when (ex.Message.Contains("download"))
		{
			Console.WriteLine("Браузер не найден. Запускаю загрузку Chromium...");
			if (DownloadChromium())
			{
				Console.WriteLine("Загрузка завершена. Повторный запуск...");
				_browser = await _playwright.Chromium.LaunchAsync(driverOptions);
			}
			else
			{
				throw new Exception("Не удалось загрузить Chromium. Проверьте подключение к сети и права доступа.");
			}
		}
	}

	// --- Управление контекстами ---

	/// <summary>
	/// Создает новый контекст браузера с указанным именем и опциями.
	/// </summary>
	/// <param name="name">Уникальное имя для идентификации контекста.</param>
	/// <param name="options">Необязательные параметры для нового контекста. Если null, используются DefaultContextOptions.</param>
	/// <returns>Созданный IBrowserContext.</returns>
	/// <exception cref="ArgumentException">Вызывается, если контекст с таким именем уже существует.</exception>
	/// <exception cref="InvalidOperationException">Вызывается, если браузер не был инициализирован.</exception>
	public async Task<IBrowserContext> CreateContextAsync(string name, BrowserNewContextOptions? options = null)
	{
		if (_browser == null)
			throw new InvalidOperationException("Браузер не инициализирован. Вызовите CreateAsync перед использованием.");

		var context = await _browser.NewContextAsync(options ?? DefaultContextOptions);

		if (!_contexts.TryAdd(name, context))
		{
			// Если не удалось добавить, значит ключ уже существует. Закрываем созданный контекст и выбрасываем исключение.
			await context.CloseAsync();
			throw new ArgumentException($"Контекст с именем '{name}' уже существует.", nameof(name));
		}

		return context;
	}

	/// <summary>
	/// Возвращает существующий контекст по имени.
	/// </summary>
	/// <param name="name">Имя контекста.</param>
	/// <returns>Объект IBrowserContext.</returns>
	/// <exception cref="KeyNotFoundException">Вызывается, если контекст с таким именем не найден.</exception>
	public IBrowserContext GetContext(string name)
	{
		if (_contexts.TryGetValue(name, out var context))
		{
			return context;
		}
		throw new KeyNotFoundException($"Контекст с именем '{name}' не найден.");
	}

	/// <summary>
	/// Асинхронно закрывает и удаляет контекст по имени.
	/// </summary>
	/// <param name="name">Имя контекста для закрытия.</param>
	/// <returns>True, если контекст был найден и закрыт; иначе False.</returns>
	public async Task<bool> CloseContextAsync(string name)
	{
		if (_contexts.TryRemove(name, out var context))
		{
			await context.CloseAsync();
			return true;
		}
		return false;
	}

	// --- Управление страницами ---

	/// <summary>
	/// Создает новую страницу в указанном контексте.
	/// </summary>
	/// <param name="contextName">Имя контекста, в котором нужно создать страницу.</param>
	/// <returns>Новая страница IPage.</returns>
	public async Task<IPage> NewPageAsync(string contextName)
	{
		var context = GetContext(contextName);
		return await context.NewPageAsync();
	}

	// --- Статические методы-помощники ---

	/// <summary>
	/// Открывает URL в указанной странице с несколькими попытками в случае неудачи.
	/// </summary>
	/// <param name="page">Страница, на которой нужно открыть URL.</param>
	/// <param name="url">URL для открытия.</param>
	/// <param name="maxRetries">Максимальное количество попыток.</param>
	/// <param name="waitUntil">Состояние, которого нужно дождаться после навигации.</param>
	/// <param name="timeout">Тайм-аут для одной попытки в миллисекундах.</param>
	/// <returns>Страница в случае успеха, иначе null.</returns>
	public static async Task<IPage?> OpenWithRetriesAsync(IPage page, string url, int maxRetries = 3, WaitUntilState waitUntil = WaitUntilState.Load, float timeout = 30_000)
	{
		for (int attempt = 1; attempt <= maxRetries; attempt++)
		{
			try
			{
				await page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = timeout });
				return page;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Попытка {attempt} открыть URL {url} провалена. Ошибка: {ex.Message}");
				if (attempt == maxRetries)
				{
					Console.WriteLine($"Превышено максимальное количество попыток ({maxRetries}) для URL: {url}");
					return null;
				}
				await Task.Delay(Random.Shared.Next(1000, 4000));
			}
		}
		return null;
	}

	/// <summary>
	/// Скачивает и устанавливает дистрибутив Chromium с помощью PowerShell.
	/// </summary>
	/// <returns>True в случае успеха, иначе False.</returns>
	public static bool DownloadChromium()
	{
		string arguments = $"-NoProfile -ExecutionPolicy ByPass -Command \"& {{ pwsh -Command \\\"playwright install chromium\\\" }}\"";

		var startInfo = new ProcessStartInfo()
		{
			FileName = "powershell.exe",
			Arguments = arguments,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		using var process = Process.Start(startInfo);
		if (process == null)
		{
			Console.WriteLine("Ошибка: не удалось запустить процесс PowerShell.");
			return false;
		}

		// Выводим лог установки в консоль
		Console.WriteLine(process.StandardOutput.ReadToEnd());
		Console.Error.WriteLine(process.StandardError.ReadToEnd());

		process.WaitForExit();
		return process.ExitCode == 0;
	}

	// --- Очистка ресурсов ---

	/// <summary>
	/// Корректно закрывает все открытые контексты, браузер и освобождает ресурсы Playwright.
	/// Автоматически вызывается при использовании `await using`.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		// Закрываем все контексты
		foreach (var contextName in _contexts.Keys)
		{
			if (_contexts.TryRemove(contextName, out var context))
			{
				await context.CloseAsync();
			}
		}

		// Закрываем браузер
		if (_browser != null)
		{
			await _browser.CloseAsync();
		}

		// Освобождаем Playwright
		_playwright?.Dispose();

		// Подавляем финализацию, так как ресурсы уже освобождены
		GC.SuppressFinalize(this);
	}
}