namespace ScraperAcesso.Sites.Editor;

using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Product;
using ScraperAcesso.Utils;

public sealed class EditorService(string url) : BaseSiteParser
{
    public static readonly List<string> ImageFormats = ["jpg", "jpeg", "png"];
    public static readonly Vector2 MinImageSize = new(156, 120);
    public static readonly Vector2 MaxImageSize = new(10000, 10000);
    public const uint MaxImageSizeInBytes = 5 * 1024 * 1024; // 5 MB
    public const ushort MaxSEOAltSymbols = 100;
    public const ushort MaxUrlLength = 64;

    private const int MaxFailuresPerProduct = 3;

    public static class XPath
    {
        public const string EnableEditorButton = "//form/button[@name='preview' and @value='0']";
        public const string AddProductButton = "//button[@title='Добавить товар']";
        public const string SearchInCatalog = "//input[@placeholder='Поиск по каталогу']";
        public const string IsLoadingSpinner = "//md-spinner";
        public const string SaveProductButton = "//footer//button[@class='-btn -btn-complete' and @data-indication-click]";
        public const string TitleInput = "//input[@data-ng-model='$ctrl.product.name']";
        public const string UrlInput = "//input[@data-ng-model='$ctrl.product.slug']";
        public const string ValidUrlInput = "//input[@data-ng-model='$ctrl.product.slug' and contains(@class, 'ng-valid-slug-exist')]";
        public const string PriceInput = "//input[@data-ng-model='$ctrl.product.cost']";
        public const string CountInput = "//input[@data-ng-model='$ctrl.product.balance']";
        public const string SEOTitleInput = "//input[@data-ng-model='$ctrl.product.seoTitle']";
        public const string SEODescriptionInput = "//input[@data-ng-model='$ctrl.product.seoMetaDesc']";
        public const string SEOKeywordsInput = "//input[@data-ng-model='$ctrl.product.seoMetaKeywords']";

        public const string NavDescriptionContainer = "//div[@data-nt-menu and contains(@class, 'menu-horizontal')][2]";
        public const string NavDescriptionLink = "//nav//a";

        public const string DeleteProductButton = "//a[@data-ng-click='$ctrl.delete()']";
        public const string ServerErrorModal = $"//div[contains(@class, '-notification') and contains(@class, 'error') and not(contains(@class, '{OperationExecutor.ProcessedMarkerClass}'))]";
        public const string ServerErrorModalText = "//div[contains(@class, '-notification-text')]";

        public static class ShortDescription
        {
            public const string TabName = "Краткое описание";
            public const string Target = "//nt-menu-content[@title='Краткое описание']";
            public const string Iframe = "//iframe";
        }

        public static class FullDescription
        {
            public const string TabName = "Полное описание";
            public const string Target = "//nt-menu-content[@title='Полное описание']";
            public const string BoldButton = "//div[@aria-label='Bold']//button";
            public const string Iframe = "//iframe";
        }

        public static class Seo
        {
            public const string TabName = "SEO";
        }

        public static class PreviewImageEditor
        {
            public const string Target = "//image-editor[@image='$ctrl.product.avatar']";
            public const string UploadButton = "//div[@data-ng-click='$ctrl.upload()']";
            public const string AltButton = "//div[@data-ng-click='$ctrl.alt()']";
        }

        public static class DetailedImageEditor
        {
            public const string Target = "//div[contains(@class, 'product-photos')]";
            public const string RawImageEditorInstance = "//image-editor";
            public const string ImageEditorInstance = "//image-editor[@data-ng-repeat and @format]";
            public const string UploadButton = "//div[@data-ng-click='$ctrl.upload()']";
        }

        public static class ImageWindowEditor
        {
            public const string Target = "//section[contains(@class, 'modal-image-editor') or @id='edit-window']";
            public const string SelectInput = "//input[@type='file']";
            public const string SaveButton = "//div[contains(@class, 'save') and @data-ng-click]";
        }

        public static class AltWindowEditor
        {
            public const string Target = "//section[contains(@class, 'modal-alt-editor') or @id='edit-window']";
            public const string Input = "//input[@type='text']";
            public const string SaveButton = "//button[@nt-indicator='isSave' or contains(., 'Сохранить')]";
        }

        public static class ValidInputClass
        {
            public const string URLInput = "ng-valid-slug-exist";
        }

        public static class InvalidInputClass
        {
            /// <summary>
            /// Для поля URL
            /// </summary>
            public const string URLIsExist = "ng-invalid-slug-exist";

            /// <summary>
            /// Для всех полей, где есть ограничение на количество символов
            /// </summary>  
            public const string InputReachedMaxLength = "ng-invalid-max-length";
        }
    }

    public override string URL { get; } = url;
    public override IPage? Page { get; protected set; }
    private OperationExecutor? _executor;

    public override async Task<bool> ParseAsync(ChromiumScraper scraper)
    {
        var productsToProcess = await BaseProduct.LoadProductsByStatusAsync(false);
        if (productsToProcess.Count == 0)
        {
            Log.Print("All products are already added. Nothing to do.");
            return true;
        }
        return await ProcessProductsAsync(productsToProcess, scraper);
    }

    public async Task<bool> ProcessProductsAsync(ICollection<BaseProduct> initialProducts, ChromiumScraper browser)
    {
        if (Page == null)
        {
            Page = await browser.NewPageAsync(Constants.Contexts.Editor);
            if (Page == null || !await OpenEditorPage(Page)) return false;
        }

        _executor = new OperationExecutor(Page);

        // --- НОВАЯ ЛОГИКА ОЧЕРЕДИ ---
        var productQueue = new List<BaseProduct>(initialProducts);
        var failureTracker = new Dictionary<BaseProduct, int>();
        int totalProducts = productQueue.Count;
        int successCount = 0;
        int permanentlyFailedCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        Log.Print($"--- Starting to process a queue of {totalProducts} products... ---");

        // Используем while, так как размер коллекции будет меняться
        while (productQueue.Count > 0)
        {
            // Берем первый товар из очереди
            var currentProduct = productQueue[0];
            productQueue.RemoveAt(0); // И сразу удаляем его, чтобы избежать бесконечного цикла при ошибках

            var productStopwatch = Stopwatch.StartNew();
            Log.Print($"--- Processing product: '{currentProduct.Title}' ({successCount + 1}/{totalProducts}) ---");

            bool success = await ProcessSingleProductAsync(currentProduct);

            if (success)
            {
                await currentProduct.MarkAsAddedAsync();
                successCount++;
                productStopwatch.Stop();
                Log.Print($"--- Successfully processed '{currentProduct.Title}' in {productStopwatch.Elapsed.TotalSeconds:F2}s ---");
            }
            else
            {
                // Увеличиваем счетчик неудач для этого товара
                failureTracker.TryGetValue(currentProduct, out int currentFailures);
                currentFailures++;
                failureTracker[currentProduct] = currentFailures;

                Log.Error($"--- Failed to process product '{currentProduct.Title}'. Failure count: {currentFailures}/{MaxFailuresPerProduct}. ---");

                if (currentFailures >= MaxFailuresPerProduct)
                {
                    // Товар "безнадежен"
                    permanentlyFailedCount++;
                    Log.Error($"--- Product '{currentProduct.Title}' has failed too many times and will be permanently skipped. ---");
                    // Ничего не делаем, он уже удален из очереди
                }
                else
                {
                    // Даем товару еще один шанс, добавляя его в конец очереди
                    Log.Warning($"--- Moving '{currentProduct.Title}' to the back of the queue. ---");
                    productQueue.Add(currentProduct);
                }
            }
        }

        totalStopwatch.Stop();
        Log.Print($"--- Job Finished in {totalStopwatch.Elapsed.TotalMinutes:F2} minutes. ---");
        Log.Print($"--- Results: {successCount} successful, {permanentlyFailedCount} permanently failed, {totalProducts} total. ---");

        // Считаем задачу успешной, если хотя бы один товар был добавлен.
        return successCount > 0;
    }


    private async Task<bool> ProcessSingleProductAsync(BaseProduct product)
    {
        bool isInEditorMode = false;
        try
        {
            await _executor!.ExecuteAsync("Navigate to Add Product Form", async () =>
            {
                Log.Print("Navigating to product creation form...");
                var searchCatalog = Page!.Locator(XPath.SearchInCatalog);
                await searchCatalog.HoverAsync();
                await Task.Delay(300);
                await searchCatalog.ClickAsync();
                await Task.Delay(225);
                await Page!.Locator(XPath.AddProductButton).ClickAsync();
                isInEditorMode = true;
            });

            await _executor.ExecuteAsync("Fill Main Details", () => FillMainDetailsAsync(product));
            await _executor.ExecuteAsync("Fill Descriptions", () => FillDescriptionsAsync(product));
            await UploadAllImagesAsync(product);
            await _executor.ExecuteAsync("Fill SEO Data", () => FillSeoDataAsync(product));
            await _executor.ExecuteAsync("Final Save", SaveProductAsync, 2);

            Log.Print($"Atomic operation for '{product.Title}' completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"CRITICAL FAILURE within single product processing for '{product.Title}'. Activating cleanup.");
            Log.Error($"Root cause: {ex.Message}");
            if (isInEditorMode)
            {
                await TryDeleteProductAsync();
            }
            if (ex.Message.Contains("Target page, context or browser has been closed"))
            {
                throw;
            }
            // Возвращаем false, чтобы внешняя логика очереди могла это обработать
            return false;
        }
    }

    private async Task SaveProductAsync()
    {
        Log.Print("Final step: Clicking 'Save' button.");
        await Page!.Locator(XPath.SaveProductButton).ClickAsync();
        // Ожидаем возврата на главную страницу редактора, это и есть подтверждение сохранения
        Log.Print("Waiting for return to the main editor page...");
        await Page.Locator(XPath.SearchInCatalog).WaitForAsync(new() { Timeout = 100_000 });
        Log.Print("Product saved, returned to the main editor page.");
    }

    private async Task<bool> TryDeleteProductAsync()
    {
        try
        {
            Log.Warning("--- Emergency Delete Protocol Activated ---");
            var deleteButton = Page!.Locator(XPath.DeleteProductButton);
            if (!await deleteButton.IsVisibleAsync())
            {
                Log.Error("Delete button is not visible. Cannot perform emergency delete. Reloading page to exit editor.");
                // Если кнопки удаления нет, возможно, мы на главной. Попробуем перезагрузиться.
                await OpenEditorPage(Page);
                return false;
            }

            Page.Dialog += async (_, dialog) =>
            {
                Log.Warning($"Browser dialog opened with message: '{dialog.Message}'. Accepting...");
                await dialog.AcceptAsync();
            };

            Log.Print("Clicking delete button and waiting for browser confirmation dialog...");
            await deleteButton.ClickAsync();

            await Page.Locator(XPath.AddProductButton).WaitForAsync(new() { Timeout = 30000 });
            Log.Print("Emergency delete successful. Unsaved product has been removed.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"FATAL: The emergency delete procedure itself failed. Error: {ex.Message}");
            return false;
        }
    }

    private async Task NavigateToEditorTabAsync(string tabName)
    {
        Log.Print($"Dispatching 'click' event to '{tabName}' tab...");
        try
        {
            var tabLink = Page!.Locator(XPath.NavDescriptionContainer)
                               .Locator(XPath.NavDescriptionLink, new() { HasText = tabName });
            await tabLink.WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await tabLink.DispatchEventAsync("click");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to navigate to tab '{tabName}'. Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Быстрое заполнение полей через прямое присваивание значения через JavaScript.
    /// </summary>
    private async Task FillInputViaJS(string selector, string value, int timeout = 45_000)
    {
        Log.Print($"Filling input '{selector}' via JS");
        await Page!.Locator(selector).WaitForAsync(new() { Timeout = timeout, State = WaitForSelectorState.Attached });
        await Page!.Locator(selector).EvaluateAsync("(element, value) => { element.value = value; element.dispatchEvent(new Event('input', { bubbles: true })); element.dispatchEvent(new Event('change', { bubbles: true })); }", value);
    }

    private async Task FillMainDetailsAsync(BaseProduct product)
    {
        Log.Print("Filling main details via JS...");
        // Обрезаем заголовок, если он слишком длинный
        string title = product.Title.Length > BaseProduct.MaxTitleLength
            ? product.Title[..BaseProduct.MaxTitleLength]
            : product.Title;
        await FillInputViaJS(XPath.TitleInput, title, 90_000);

        // Используем новый умный метод для генерации и проверки URL
        await GenerateValidUrlSlugAsync(product.TranslitedTitle);

        await FillInputViaJS(XPath.PriceInput, product.Price.ToString());
        await FillInputViaJS(XPath.CountInput, product.Count.ToString());
        Log.Print("Finished filling main details.");
    }

    /// <summary>
    /// Генерирует, проверяет и вставляет валидный URL, 
    /// обрабатывая дубликаты и ограничения по длине.
    /// </summary>
    private async Task GenerateValidUrlSlugAsync(string baseSlug)
    {
        // 1. Обрезаем базовый slug до максимальной длины
        if (baseSlug.Length > MaxUrlLength)
        {
            baseSlug = baseSlug[..MaxUrlLength];
        }

        var urlInput = Page!.Locator(XPath.UrlInput);
        string currentSlug = baseSlug;
        uint attempt = 2;

        while (true)
        {
            await urlInput.FillAsync(currentSlug);
            try
            {
                if (await Page.WaitForSelectorAsync(XPath.ValidUrlInput, new() { Timeout = 3000, State = WaitForSelectorState.Attached }) == null)
                {
                    Log.Warning($"URL slug '{currentSlug}' ({currentSlug.Length}) is invalid (Second condition), fixing...");
                }
                else
                {
                    Log.Print($"URL slug '{currentSlug}' ({currentSlug.Length}) is valid (First condition).");
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"URL slug '{currentSlug}' ({currentSlug.Length}) is invalid (First condition: '{ex.Message}'), fixing...");
            }

            // Проверяем, появился ли у поля класс ошибки о дубликате
            string? classes = await urlInput.GetAttributeAsync("class");
            if (classes != null)
            {
                if (classes.Contains(XPath.InvalidInputClass.URLIsExist))
                {
                    Log.Warning($"URL slug '{currentSlug}' already exists. Generating a new one...");

                    // Формируем новый slug с суффиксом, например, "my-product-2"
                    string suffix = $"-{attempt++}";
                    int availableLength = MaxUrlLength - suffix.Length;
                    // Укорачиваем базу, если нужно, чтобы влез суффикс
                    string trimmedBase = baseSlug.Length > availableLength ? baseSlug[..availableLength] : baseSlug;
                    currentSlug = trimmedBase + suffix;
                }
                else if (classes.Contains(XPath.ValidInputClass.URLInput))
                {
                    Log.Print($"URL slug '{currentSlug}' ({currentSlug.Length}) is valid (Second condition).");
                    break;
                }
                else
                {
                    Log.Print($"URL slug '{currentSlug}' is valid and has been set.");
                }
            }
            else
            {
                Log.Error($"FATAL: Failed to get classes for URL input");
                attempt++;
                await Task.Delay(3000);
            }
        }
    }

    private async Task FillDescriptionsAsync(BaseProduct product)
    {
        // Краткое описание
        if (!string.IsNullOrWhiteSpace(product.ShortDescription))
        {
            await NavigateToEditorTabAsync(XPath.ShortDescription.TabName);
            Log.Print("Locating iframe for short description...");
            var frameLocator = Page!.Locator(XPath.ShortDescription.Target).FrameLocator(XPath.ShortDescription.Iframe);
            var editableBody = frameLocator.Locator("body");
            await editableBody.WaitForAsync();

            Log.Print("Filling short description with human-like input...");
            // FillAsync в Playwright достаточно умен, чтобы сгенерировать нужные события
            await editableBody.FillAsync(product.ShortDescription);
            Log.Print("Short description filled.");
        }
        else
        {
            Log.Warning($"Short description is empty for {product.URL} ({product.TranslitedTitle}). Skipping...");
        }

        // Полное описание
        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            await NavigateToEditorTabAsync(XPath.FullDescription.TabName);
            Log.Print("Locating iframe for full description...");
            var frameLocator = Page!.Locator(XPath.FullDescription.Target).FrameLocator(XPath.FullDescription.Iframe);
            var editableBody = frameLocator.Locator("body");
            await editableBody.WaitForAsync();

            Log.Print("Filling full description with human-like input...");
            // 1. Вставляем основное описание
            await editableBody.FillAsync(product.Description);

            var attributes = product.GetAttributesAsString();
            if (!string.IsNullOrWhiteSpace(attributes))
            {
                LocatorPressSequentiallyOptions sequentiallyOptions = new() { Delay = 7 };

                // 2. Добавляем переносы строк и печатаем заголовок "Характеристики"
                await editableBody.PressAsync("End"); // Перемещаем курсор в конец
                await editableBody.PressAsync("Enter");
                await editableBody.PressAsync("Enter");
                await editableBody.PressSequentiallyAsync("Характеристики", sequentiallyOptions); // TypeAsync имитирует печать
                Log.Print("'Характеристики' header typed.");

                // 3. Выделяем заголовок и делаем его жирным
                try
                {
                    Log.Print("Attempting to apply bold style...");
                    await editableBody.PressAsync("Shift+Home"); // Выделяем всю строку, где сейчас курсор
                    var boldButton = Page.Locator(XPath.FullDescription.Target).Locator(XPath.FullDescription.BoldButton);
                    await boldButton.ClickAsync(); // Добавляем жирный шрифт
                    await editableBody.PressAsync("End"); // Снова в конец
                    await Task.Delay(350); // Даем время на обработку
                    await boldButton.ClickAsync(); // Отменяем выделение
                    await Task.Delay(350); // Даем время на обработку
                    Log.Print("Bold style applied.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Could not apply bold style. This is a non-critical error. Details: {ex.Message}");
                }

                // 4. Вставляем сами атрибуты
                await editableBody.PressAsync("Enter");
                await editableBody.PressAsync("Enter");
                await editableBody.PressSequentiallyAsync(attributes, sequentiallyOptions);
                Log.Print("Attributes text filled.");
            }
            Log.Print("Full description filled.");
        }
        else
        {
            Log.Warning($"Full description is empty for {product.URL} ({product.TranslitedTitle}). Skipping...");
        }
    }

    private async Task FillSeoDataAsync(BaseProduct product)
    {
        if (product.SEO == null)
        {
            Log.Warning($"SEO data is empty for {product.URL} ({product.TranslitedTitle}). Skipping...");
            return;
        }

        await NavigateToEditorTabAsync(XPath.Seo.TabName);
        Log.Print("Filling SEO data via JS...");

        // Обрезаем данные, если они превышают лимиты, чтобы избежать ошибок валидации
        string seoTitle = product.SEO.Title.Length > SEOProductInfo.MaxTitleLength
            ? product.SEO.Title[..SEOProductInfo.MaxTitleLength]
            : product.SEO.Title;

        string seoSentence = product.SEO.SeoSentence.Length > SEOProductInfo.MaxSeoSentenceLength
            ? product.SEO.SeoSentence[..SEOProductInfo.MaxSeoSentenceLength]
            : product.SEO.SeoSentence;

        string seoKeywords = product.SEO.Keywords.Length > SEOProductInfo.MaxKeywordsLength
            ? product.SEO.Keywords[..SEOProductInfo.MaxKeywordsLength]
            : product.SEO.Keywords;

        await FillInputViaJS(XPath.SEOTitleInput, seoTitle);
        // Для SEO Описания XPath указывает на textarea, а не input
        await Page!.Locator(XPath.SEODescriptionInput).EvaluateAsync("(element, value) => { element.value = value; element.dispatchEvent(new Event('input', { bubbles: true })); }", seoSentence);
        await FillInputViaJS(XPath.SEOKeywordsInput, seoKeywords);
        Log.Print("Finished filling SEO data.");
    }

    private async Task UploadAllImagesAsync(BaseProduct product)
    {
        if (product.AllImages == null || product.AllImages.Count == 0)
        {
            Log.Print("No images found for this product. Skipping image upload.");
            return;
        }

        List<string> validImages = FilterValidImages(product.AllImages);
        if (validImages.Count == 0)
        {
            Log.Print("No valid images found after filtering. Skipping image upload.");
            return;
        }

        string altText = product.Title.Length > MaxSEOAltSymbols ? product.Title[..MaxSEOAltSymbols] : product.Title;

        // Загрузка превью - это критическая операция
        await _executor!.ExecuteAsync("Upload Preview Image",
            () => UploadSingleImage(XPath.PreviewImageEditor.Target, validImages[0], altText));

        // Загрузка детальных изображений
        for (int i = 1; i < validImages.Count; i++)
        {
            try
            {
                var validImage = validImages.ElementAtOrDefault(i);
                if (validImage == null) break;

                await _executor.ExecuteAsync($"Upload Detailed Image #{i}/{validImages.Count - 1}",
                    () => UploadSingleImage(XPath.DetailedImageEditor.Target, validImage, altText, isDetailed: true));
            }
            catch (OperationExecutor.ExceedImagesLimitException ex)
            {
                // Ловим КОНКРЕТНУЮ ошибку о превышении лимита
                Log.Warning(ex.Message);
                Log.Warning("Maximum image limit reached. Stopping further image uploads for this product.");
                break; // Прерываем цикл, но НЕ считаем это критической ошибкой
            }
        }
    }

    private async Task UploadSingleImage(string containerXPath, string imagePath, string altText, bool isDetailed = false)
    {
        Log.Print($"Uploading image '{imagePath}'...");
        var container = Page!.Locator(containerXPath);

        // Для детальных фото мы ищем последнюю "пустую" ячейку, для превью - единственную
        var rawImageEditorTarget = isDetailed ? container.Locator(XPath.DetailedImageEditor.RawImageEditorInstance).Last : container;

        // 1. Открываем окно загрузки
        var uploadButton = isDetailed ? rawImageEditorTarget.Locator(XPath.DetailedImageEditor.UploadButton) : rawImageEditorTarget.Locator(XPath.PreviewImageEditor.UploadButton);
        await uploadButton.DispatchEventAsync("click");

        var imageWindow = Page.Locator(XPath.ImageWindowEditor.Target);
        await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // 2. Выбираем файл и сохраняем
        await imageWindow.Locator(XPath.ImageWindowEditor.SelectInput).SetInputFilesAsync(imagePath);

        // Для детальных изображений кнопка "сохранить" не нужна, окно закрывается само
        if (!isDetailed)
        {
            Log.Print("Clicking save button for preview image...");
            var saveButton = imageWindow.Locator(XPath.ImageWindowEditor.SaveButton);
            await saveButton.ClickAsync(new() { Timeout = 180_000 });
        }

        Log.Print("Waiting for image upload to complete...");
        await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 180_000 });
        Log.Print($"Image '{Path.GetFileName(imagePath)}' uploaded.");

        // 3. Устанавливаем Alt-текст
        Log.Print("Waiting for image editor to appear...");
        var imageEditorTarget = isDetailed ? Page.Locator(XPath.DetailedImageEditor.ImageEditorInstance).Last : rawImageEditorTarget;

        Log.Print("Setting alt text...");
        var altButton = imageEditorTarget.Locator(XPath.PreviewImageEditor.AltButton);
        await altButton.DispatchEventAsync("click");

        var altWindow = Page.Locator(XPath.AltWindowEditor.Target);
        await altWindow.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await altWindow.Locator(XPath.AltWindowEditor.Input).FillAsync(altText);
        await altWindow.Locator(XPath.AltWindowEditor.SaveButton).ClickAsync();
        await altWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Log.Print("Alt text set.");
    }

    private static List<string> FilterValidImages(List<string> imagePaths)
    {
        Log.Print($"Filtering images. Initial count: {imagePaths.Count}. Max allowed: {BaseProduct.MaxImagesCount}.");
        var validImages = new List<string>();
        foreach (var path in imagePaths)
        {
            if (validImages.Count >= BaseProduct.MaxImagesCount)
            {
                Log.Warning($"Image limit ({BaseProduct.MaxImagesCount}) reached. Skipping remaining images.");
                break;
            }

            try
            {
                // Проверяем существование файла
                if (!File.Exists(path))
                {
                    Log.Warning($"Image file not found, skipping: {path}");
                    continue;
                }

                // Проверяем размер файла
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > MaxImageSizeInBytes)
                {
                    Log.Warning($"Image is too large ({fileInfo.Length / 1024} KB), skipping: {path}");
                    continue;
                }

                var dimensions = ImageUtils.GetImageDimensions(path);

                // Проверяем, входят ли размеры в допустимый диапазон
                if (!ImageUtils.IsResolutionInRange(dimensions, MinImageSize, MaxImageSize))
                {
                    Log.Warning($"Image has invalid dimensions ({dimensions.X}x{dimensions.Y}), skipping: {path}");
                    continue;
                }

                // Если все проверки пройдены, добавляем изображение.
                validImages.Add(path);
            }
            catch (Exception ex)
            {
                // Ловим любые ошибки (файл не найден, формат не поддерживается, файл поврежден)
                // и просто логируем их, продолжая работу со следующим файлом.
                Log.Warning($"Could not validate image '{path}'. It will be skipped. Reason: {ex.Message}");
            }
        }
        Log.Print($"Filtering complete. Valid images found: {validImages.Count}.");
        return validImages;
    }

    private async Task<bool> OpenEditorPage(IPage page)
    {
        Log.Print($"Attempting to open editor URL: {URL}");
        var loadedPage = await ChromiumScraper.OpenWithRetriesAsync(page, URL);
        if (loadedPage == null)
        {
            Log.Error($"Failed to open editor page after retries: {URL}");
            return false;
        }
        Page = loadedPage; // Обновляем ссылку на страницу
        Log.Print("Page opened. Checking for 'Enable Editor' button...");

        var enableButton = Page.Locator(XPath.EnableEditorButton).First;
        await enableButton.DispatchEventAsync("click");

        Log.Print("Waiting for main editor interface to load (checking for 'Add Product' button)...");
        await Page.Locator(XPath.AddProductButton).WaitForAsync(new() { Timeout = 20000, State = WaitForSelectorState.Attached });
        Log.Print("Editor interface loaded successfully.");
        return true;
    }

    #region Helper Class: OperationExecutor

    private sealed class OperationExecutor(in IPage page)
    {
        public const string ProcessedMarkerClass = "processed-by-scraper";

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
                    await completedTask;

                    Log.Print($"--- Operation '{operationName}' completed successfully. ---");
                    return;
                }
                catch (Exception ex)
                {
                    // Если это ошибка, которую нужно игнорировать (как лимит картинок),
                    // она будет поймана выше, в вызывающем коде.
                    // Сюда дойдут только те ошибки, которые требуют повтора или полного провала.

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
                    var errorModal = _page.Locator(XPath.ServerErrorModal);

                    if (await errorModal.IsVisibleAsync())
                    {
                        string? errorText = (await errorModal.InnerTextAsync()).ToLower();
                        Log.Warning($"Warning watcher detected a visible error modal! Text: {errorText.Trim()}");

                        await errorModal.EvaluateAsync($"(element) => element.classList.add('{ProcessedMarkerClass}')");
                        Log.Print($"Marked error modal with class '{ProcessedMarkerClass}' to prevent re-triggering.");

                        // --- РАСПОЗНАВАНИЕ ТИПА ОШИБКИ ---
                        if (errorText == "максимальное количество картинок:12")
                        {
                            // Выбрасываем специальное исключение для этой ошибки
                            throw new ExceedImagesLimitException();
                        }
                        else if (errorText.Contains("internal server error"))
                        {
                            // Выбрасываем исключение для серверной ошибки, требующей повтора
                            throw new ServerErrorException(errorText);
                        }

                        // Для всех остальных непредвиденных модальных окон
                        // throw new UndefinedModalErrorException(errorText);
                    }
                    await Task.Delay(500, token);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception) { throw; } // Пробрасываем распознанную ошибку наверх
            }
        }

        // --- КАСТОМНЫЕ ИСКЛЮЧЕНИЯ ДЛЯ РАЗНЫХ ТИПОВ ОШИБОК ---

        /// <summary> Ошибка, которую нужно обработать как некритичную и продолжить выполнение. </summary>
        public sealed class ExceedImagesLimitException() : Exception($"Exceeded the maximum number of images allowed ({BaseProduct.MaxImagesCount}).");

        /// <summary> Серверная ошибка, требующая паузы и повторной попытки. </summary>
        public sealed class ServerErrorException(string message) : Exception($"Server error modal appeared: {message}");

        /// <summary> Неизвестная ошибка в модальном окне, которая должна остановить операцию. </summary>
        public sealed class UndefinedModalErrorException(string message) : Exception($"An undefined error modal appeared: {message}");
    }
    #endregion
}