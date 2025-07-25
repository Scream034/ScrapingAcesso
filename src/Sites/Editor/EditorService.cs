namespace ScraperAcesso.Sites.Editor;

using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
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
    public const uint MaxImageSizeInBytes = 20_971_520; // 20 MB
    public const ushort MaxSEOAltSymbols = 100;
    public const ushort MaxUrlLength = 100;

    public static class XPath
    {
        public const string EnableEditorButton = "//form/button[@name='preview' and @value='0']";
        public const string AddProductButton = "//button[@title='Добавить товар']";
        public const string SearchInCatalog = "//input[@placeholder='Поиск по каталогу']";
        public const string IsLoadingSpinner = "//md-spinner";
        public const string SaveProductButton = "//footer//button[@class='-btn -btn-complete' and @data-indication-click]";
        public const string TitleInput = "//input[@data-ng-model='$ctrl.product.name']";
        public const string UrlInput = "//input[@data-ng-model='$ctrl.product.slug']";
        public const string PriceInput = "//input[@data-ng-model='$ctrl.product.cost']";
        public const string CountInput = "//input[@data-ng-model='$ctrl.product.balance']";
        public const string SEOTitleInput = "//input[@data-ng-model='$ctrl.product.seoTitle']";
        public const string SEODescriptionInput = "//input[@data-ng-model='$ctrl.product.seoMetaDesc']";
        public const string SEOKeywordsInput = "//input[@data-ng-model='$ctrl.product.seoMetaKeywords']";

        public const string NavDescriptionContainer = "//div[@data-nt-menu and contains(@class, 'menu-horizontal')][2]";
        public const string NavDescriptionLink = "//nav//a";

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

    /// <summary>
    /// Точка входа по умолчанию. Загружает ВСЕ необработанные товары и запускает их обработку.
    /// </summary>
    public override async Task<bool> ParseAsync(ChromiumScraper scraper)
    {
        var productsToProcess = await BaseProduct.LoadProductsByStatusAsync(false);
        if (productsToProcess.Count == 0)
        {
            Log.Print("All products are already added. Nothing to do.");
            return true;
        }
        // Вызываем новый основной метод с полным списком товаров
        return await ProcessProductsAsync(productsToProcess, scraper);
    }

    public async Task<bool> ProcessProductsAsync(ICollection<BaseProduct> productsToProcess, ChromiumScraper browser)
    {
        if (Page == null)
        {
            Page = await browser.NewPageAsync(Constants.Contexts.Editor);
            if (Page == null || !await OpenEditorPage(Page)) return false;
        }

        Log.Print($"Starting to process {productsToProcess.Count} products...");
        int successCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var product in productsToProcess)
        {
            var productStopwatch = Stopwatch.StartNew();
            Log.Print($"--- Processing product: '{product.Title}' ---");

            try
            {
                // Запускаем полный цикл добавления для одного товара
                bool success = await ProcessSingleProductAsync(product);
                if (success)
                {
                    await product.MarkAsAddedAsync(); // Помечаем как добавленный только в случае полного успеха
                    successCount++;
                    productStopwatch.Stop();
                    Log.Print($"--- Successfully processed '{product.Title}' in {productStopwatch.Elapsed.TotalSeconds:F2}s ---");
                }
                else
                {
                    Log.Error($"--- Failed to process product '{product.Title}'. Skipping. ---");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"A critical error occurred while processing '{product.Title}'. Error: {ex.Message}");
                // Попробуем перезагрузить страницу, чтобы восстановиться для следующего товара
                await OpenEditorPage(Page);
            }
        }

        totalStopwatch.Stop();
        Log.Print($"--- Job Finished. Successfully added {successCount}/{productsToProcess.Count} products in {totalStopwatch.Elapsed.TotalMinutes:F2} minutes. ---");
        return successCount > 0;
    }


    private async Task<bool> ProcessSingleProductAsync(BaseProduct product)
    {
        try
        {
            Log.Print("Hover and click on the 'Search Catalog'...");
            var searchCatalog = Page!.Locator(XPath.SearchInCatalog);
            await searchCatalog.HoverAsync();
            await Task.Delay(300);
            await searchCatalog.ClickAsync();
            await Task.Delay(225);

            Log.Print("Click on the 'Add Product'...");
            var addProductButton = Page!.Locator(XPath.AddProductButton);
            await Page!.Locator(XPath.AddProductButton).ClickAsync();
            addProductButton = null;

            // 1. Заполнение основных полей
            Log.Print("--- Step 1/5: Filling Main Details (JS Mode) ---");
            await FillMainDetailsAsync(product);

            // 2. Заполнение описаний
            Log.Print("--- Step 2/5: Filling Descriptions (Human-Like Mode) ---");
            await FillDescriptionsAsync(product);

            // 3. Загрузка изображений и установка Alt-тегов
            Log.Print("--- Step 3/5: Uploading Images (Event Mode) ---");
            await UploadAllImagesAsync(product);

            // 4. Заполнение SEO-данных
            Log.Print("--- Step 4/5: Filling SEO Data (JS Mode) ---");
            await FillSeoDataAsync(product);

            // 5. Сохранение всего продукта
            Log.Print("--- Step 5/5: Saving Product (Event Mode) ---");
            var saveButton = Page.Locator(XPath.SaveProductButton);
            await saveButton.WaitForAsync();

            Log.Print($"Waiting for the product {Page.Url} to be saved...");
            await saveButton.ClickAsync();

            await Page.Locator(XPath.IsLoadingSpinner).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 100_000 });

            Log.Print("Product saved successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"A critical, unhandled error occurred while processing '{product.Title}'. Activating emergency save protocol.");
            Log.Error($"Error Details: {ex.Message}");

            // --- АВАРИЙНОЕ СОХРАНЕНИЕ ---
            bool emergencySaveSuccess = await EmergencySaveWithErrorAsync(product, ex);

            if (emergencySaveSuccess)
            {
                Log.Warning("Emergency save succeeded. Product was saved with an error message in its title for manual review.");
            }
            else
            {
                Log.Error($"IMPORTANT. The product '{product.Title}' could not be saved at all. Manual intervention is required immediately.");
            }

            // Возвращаем false, т.к. основной процесс не был успешным
            return false;
        }
    }

    private async Task<bool> EmergencySaveWithErrorAsync(BaseProduct product, Exception exception)
    {
        try
        {
            Log.Print("--- Emergency Save Protocol Activated ---");

            // Формируем аварийное название
            string errorPrefix = "[ERROR-404] ";
            string errorMessage = exception.Message.Length > 150 ? exception.Message[..150] : exception.Message;
            string rawTitle = $"{errorPrefix}{errorMessage} | {product.Title}";

            // Обрезаем до максимальной длины поля
            string emergencyTitle = rawTitle.Length > BaseProduct.MaxTitleLength
                ? rawTitle[..BaseProduct.MaxTitleLength]
                : rawTitle;

            Log.Print($"Generated emergency title: '{emergencyTitle}'");

            // Пытаемся заполнить только поле с названием
            var titleInput = Page!.Locator(XPath.TitleInput);
            // Проверяем, видимо ли поле. Если нет, то, возможно, мы даже не на странице редактора.
            if (!await titleInput.IsVisibleAsync())
            {
                Log.Error("Title input is not visible. Cannot perform emergency save. The page might be in an invalid state.");
                return false;
            }

            await FillInputViaJS(XPath.TitleInput, emergencyTitle);

            // Пытаемся нажать "Сохранить"
            var saveButton = Page.Locator(XPath.SaveProductButton);
            if (!await saveButton.IsVisibleAsync())
            {
                Log.Error("Save button is not visible. Cannot perform emergency save.");
                return false;
            }

            await saveButton.ClickAsync();

            // Ждем завершения
            await Page.WaitForSelectorAsync(XPath.AddProductButton, new() { State = WaitForSelectorState.Attached, Timeout = 90_000 });

            return true;
        }
        catch (Exception emergencyEx)
        {
            // Если даже аварийное сохранение не удалось
            Log.Error($"FATAL: The emergency save procedure itself failed. Error: {emergencyEx.Message}");
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
        int attempt = 2;

        while (true)
        {
            await FillInputViaJS(XPath.UrlInput, currentSlug);
            // Даем AngularJS время на асинхронную валидацию (очень важно!)
            await Task.Delay(250);

            // Проверяем, появился ли у поля класс ошибки о дубликате
            string? classes = await urlInput.GetAttributeAsync("class");
            if (classes != null && classes.Contains(XPath.InvalidInputClass.URLIsExist))
            {
                Log.Warning($"URL slug '{currentSlug}' already exists. Generating a new one...");

                // Формируем новый slug с суффиксом, например, "my-product-2"
                string suffix = $"-{attempt++}";
                int availableLength = MaxUrlLength - suffix.Length;
                // Укорачиваем базу, если нужно, чтобы влез суффикс
                string trimmedBase = baseSlug.Length > availableLength ? baseSlug[..availableLength] : baseSlug;
                currentSlug = trimmedBase + suffix;
            }
            else
            {
                // Ошибки нет, URL свободен
                Log.Print($"URL slug '{currentSlug}' is valid and has been set.");
                break;
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
                    await Task.Delay(400); // Даем время на обработку
                    await boldButton.ClickAsync(); // Отменяем выделение
                    await Task.Delay(400); // Даем время на обработку
                    Log.Print("Bold style applied.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Could not apply bold style. This is a non-critical error. Details: {ex.Message}");
                }

                // 4. Вставляем сами атрибуты
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
        if (product.AllImages == null || product.AllImages.Count == 0) return;
        Log.Print("Starting image upload process (Event Mode)...");

        List<string> validImages = FilterValidImages(product.AllImages);
        if (validImages.Count == 0) return;

        string altText = product.Title.Length > MaxSEOAltSymbols ? product.Title[..MaxSEOAltSymbols] : product.Title;

        // 1. Загрузка превью
        await UploadSingleImageAsync(Page!.Locator(XPath.PreviewImageEditor.Target), XPath.PreviewImageEditor.UploadButton, validImages[0]);
        await SetAltTextAsync(Page!.Locator(XPath.PreviewImageEditor.Target), XPath.PreviewImageEditor.AltButton, altText);

        // 2. Загрузка детальных изображений
        var detailedImagesContainer = Page.Locator(XPath.DetailedImageEditor.Target);
        for (int i = 1; i < validImages.Count; i++)
        {
            var validImage = validImages[i];

            Log.Print($"Uploading image {i + 1} ({validImage}) of {validImages.Count}...");
            await detailedImagesContainer.Locator(XPath.DetailedImageEditor.UploadButton).DispatchEventAsync("click");

            var imageWindow = Page.Locator(XPath.ImageWindowEditor.Target);
            await imageWindow.WaitForAsync();
            await imageWindow.Locator(XPath.ImageWindowEditor.SelectInput).SetInputFilesAsync(validImage);
            // Сразу же сохранит, поэтому ждём закрытия (пока загрузятся на сервер редактора)
            Log.Print("Image uploaded. Waiting for image to be processed...");
            await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 200_000 });

            var lastImageEditor = detailedImagesContainer.Locator(XPath.DetailedImageEditor.ImageEditorInstance).Last;
            await SetAltTextAsync(lastImageEditor, "self", altText);
        }
    }

    private async Task UploadSingleImageAsync(ILocator container, string uploadButtonXPath, string imagePath)
    {
        await container.Locator(uploadButtonXPath).DispatchEventAsync("click");

        Log.Print($"Single image {imagePath} uploading...");
        var imageWindow = Page!.Locator(XPath.ImageWindowEditor.Target);
        await imageWindow.WaitForAsync();
        await imageWindow.Locator(XPath.ImageWindowEditor.SelectInput).SetInputFilesAsync(imagePath);

        Log.Print($"Single image uploaded. Waiting for image to be processed...");
        await imageWindow.Locator(XPath.ImageWindowEditor.SaveButton).ClickAsync(new() { Timeout = 200_000 });

        Log.Print("Closing image window...");
        await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 200_000 });
    }

    private async Task SetAltTextAsync(ILocator container, string altButtonXPath, string altText)
    {
        Log.Print($"Setting alt text as {altButtonXPath}...");

        ILocator altButton = (altButtonXPath == "self")
            ? container.Locator(XPath.PreviewImageEditor.AltButton)
            : container.Locator(altButtonXPath);

        await altButton.DispatchEventAsync("click");
        var altWindow = Page!.Locator(XPath.AltWindowEditor.Target);
        await altWindow.WaitForAsync();
        await altWindow.Locator(XPath.AltWindowEditor.Input).EvaluateAsync("(element, value) => { element.value = value; element.dispatchEvent(new Event('input', { bubbles: true })); }", altText);
        await altWindow.Locator(XPath.AltWindowEditor.SaveButton).DispatchEventAsync("click");
        await altWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden });

        Log.Print("Alt text set.");
    }

    private static List<string> FilterValidImages(List<string> imagePaths)
    {
        Log.Print($"Filtering images. Initial count: {imagePaths.Count}. Max allowed: {BaseProduct.MaxImageCount}.");
        var validImages = new List<string>();
        foreach (var path in imagePaths)
        {
            if (validImages.Count >= BaseProduct.MaxImageCount)
            {
                Log.Warning($"Image limit ({BaseProduct.MaxImageCount}) reached. Skipping remaining images.");
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
}