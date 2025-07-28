namespace ScraperAcesso.Sites.Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using ScraperAcesso.Ai;
using ScraperAcesso.Components;
using ScraperAcesso.Components.Log;
using ScraperAcesso.Components.Settings;
using ScraperAcesso.Product;
using ScraperAcesso.Utils;

public sealed class EditorService(string url) : BaseSiteParser
{
    // --- Constants ---
    public static readonly List<string> ImageFormats = ["jpg", "jpeg", "png"];
    public static readonly Vector2 MinImageSize = new(156, 120);
    public static readonly Vector2 MaxImageSize = new(10000, 10000);
    public const uint MaxImageSizeInBytes = 5 * 1024 * 1024; // 5 MB
    public const ushort MaxSEOAltSymbols = 100;
    public const ushort MaxUrlLength = 64;
    private const int MaxFailuresPerProduct = 3;

    // --- Selectors ---
    #region Selectors (XPath and CSS)

    public static class XPath
    {
        public const string EnableEditorButton = "//form/button[@name='preview' and @value='0']";
        public const string AddProductButton = "//button[@title='Добавить товар']";
        public const string SearchInCatalog = "//input[@placeholder='Поиск по каталогу']";
        public const string SaveProductButton = "//footer//button[@class='-btn -btn-complete' and @data-indication-click and not(contains(@class, '-indication-event'))]";
        public const string SaveProductProcessingButton = "//footer//button[@data-indication-click and contains(@class, '-indication-event') and contains(@class, '-btn')]";
        public const string DeleteProductButton = "//a[@data-ng-click='$ctrl.delete()']";
        public const string ServerErrorModal = $"//div[contains(@class, '-notification') and contains(@class, 'error') and not(contains(@class, '{CssClasses.Internal.ProcessedByScraper}'))]";

        // Main Details
        public const string TitleInput = "//input[@data-ng-model='$ctrl.product.name']";
        public const string UrlInput = "//input[@data-ng-model='$ctrl.product.slug']";
        public const string PriceInput = "//input[@data-ng-model='$ctrl.product.cost']";
        public const string CountInput = "//input[@data-ng-model='$ctrl.product.balance']";

        // Navigation & Tabs
        public const string NavDescriptionContainer = "//div[@data-nt-menu and contains(@class, 'menu-horizontal')][2]";
        public const string NavDescriptionLink = "//nav//a"; // Relative to container

        // Descriptions
        public static class ShortDescription
        {
            public const string TabName = "Краткое описание";
            public const string Container = "//nt-menu-content[@title='Краткое описание']";
            public const string Iframe = "//iframe"; // Relative to container
            public const string TextareaValidator = "//textarea"; // Relative to container
        }

        public static class FullDescription
        {
            public const string TabName = "Полное описание";
            public const string Container = "//nt-menu-content[@title='Полное описание']";
            public const string Iframe = "//iframe"; // Relative to container
            public const string BoldButton = "//div[@aria-label='Bold']//button"; // Relative to container
        }

        // SEO
        public static class Seo
        {
            public const string TabName = "SEO";
            public const string TitleInput = "//input[@data-ng-model='$ctrl.product.seoTitle']";
            public const string DescriptionInput = "//input[@data-ng-model='$ctrl.product.seoMetaDesc']";
            public const string KeywordsInput = "//input[@data-ng-model='$ctrl.product.seoMetaKeywords']";
        }

        // Image Editors
        public static class PreviewImageEditor
        {
            public const string Container = "//image-editor[@image='$ctrl.product.avatar']";
            public const string UploadButton = "//div[@data-ng-click='$ctrl.upload()']";
            public const string AltButton = "//div[@data-ng-click='$ctrl.alt()']";
        }

        public static class DetailedImageEditor
        {
            public const string Container = "//div[contains(@class, 'product-photos')]";
            public const string RawImageEditorInstance = "//image-editor"; // For finding the last empty slot
            public const string FilledImageEditorInstance = "//image-editor[@data-ng-repeat and @format]"; // For finding the last uploaded image
            public const string UploadButton = "//div[@data-ng-click='$ctrl.upload()']";
        }

        // Modal Windows
        public static class ImageWindowEditor
        {
            public const string Container = "//section[contains(@class, 'modal-image-editor') or @id='edit-window']";
            public const string SelectInput = "//input[@type='file']";
            public const string SaveButton = "//div[contains(@class, 'save') and @data-ng-click]";
        }

        public static class AltWindowEditor
        {
            public const string Container = "//section[contains(@class, 'modal-alt-editor') or @id='edit-window']";
            public const string Input = "//input[@type='text']";
            public const string SaveButton = "//button[@nt-indicator='isSave' or contains(., 'Сохранить')]";
        }
    }

    /// <summary>
    /// Contains CSS class names used for validation and state checks.
    /// </summary>
    public static class CssClasses
    {
        public static class Valid
        {
            public const string UrlNotEmpty = "ng-not-empty";
        }

        public static class Invalid
        {
            public const string UrlEmpty = "ng-empty";
            public const string UrlExists = "ng-invalid-slug-exist";
            public const string UrlTooLong = "ng-invalid-max-length";
            public const string MaxLength = "ng-invalid-max-length";
            public const string MaxLengthNg = "ng-invalid-nt-maxlength";
        }

        public static class Internal
        {
            public const string ProcessedByScraper = "processed-by-scraper";
        }
    }
    #endregion

    // --- Properties ---
    public override string URL { get; } = url;
    public override IPage? Page { get; protected set; }
    private OperationExecutor? _executor;

    // --- Main Logic ---
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
        var productQueue = new List<BaseProduct>(initialProducts);
        var failureTracker = new Dictionary<BaseProduct, int>();
        int totalProducts = productQueue.Count;
        int successCount = 0;
        int permanentlyFailedCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        Log.Print($"--- Starting to process a queue of {totalProducts} products... ---");

        while (productQueue.Count > 0)
        {
            if (Page == null || Page.IsClosed)
            {
                Log.Print("Page is not initialized or was closed. Creating a new one...");
                Page = await browser.NewPageAsync(Constants.Contexts.Editor);
                if (Page == null || !await OpenEditorPage(Page))
                {
                    Log.Error("FATAL: Could not initialize or reopen the editor page. Aborting process.");
                    return false;
                }
                _executor = new OperationExecutor(Page);
            }

            var currentProduct = productQueue[0];
            productQueue.RemoveAt(0);

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
                failureTracker.TryGetValue(currentProduct, out int currentFailures);
                currentFailures++;
                failureTracker[currentProduct] = currentFailures;

                Log.Error($"--- Failed to process product '{currentProduct.Title}'. Failure count: {currentFailures}/{MaxFailuresPerProduct}. ---");

                if (currentFailures >= MaxFailuresPerProduct)
                {
                    permanentlyFailedCount++;
                    Log.Error($"--- Product '{currentProduct.Title}' has failed too many times and will be permanently skipped. ---");
                }
                else
                {
                    Log.Warning($"--- Initiating full reset protocol before re-queuing '{currentProduct.Title}'. ---");
                    try
                    {
                        if (Page != null && !Page.IsClosed) await Page.CloseAsync();
                    }
                    catch (Exception pageCloseEx)
                    {
                        Log.Error($"Non-critical error while closing page during reset: {pageCloseEx.Message}");
                    }
                    finally
                    {
                        Page = null;
                    }

                    Log.Print("Waiting for 1 minute before next attempt to allow system to stabilize...");
                    await Task.Delay(TimeSpan.FromMinutes(1));

                    Log.Warning($"--- Moving '{currentProduct.Title}' to the back of the queue for another attempt. ---");
                    productQueue.Add(currentProduct);
                }
            }
        }

        totalStopwatch.Stop();
        Log.Print($"--- Job Finished in {totalStopwatch.Elapsed.TotalMinutes:F2} minutes. ---");
        Log.Print($"--- Results: {successCount} successful, {permanentlyFailedCount} permanently failed, {totalProducts} total. ---");

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

            bool isSaved = await AttemptSaveAndVerifyAsync(product);
            if (!isSaved)
            {
                // Это условие теперь почти недостижимо для ошибок валидации,
                // так как AttemptSaveAndVerifyAsync выбросит ValidationException.
                // Но оставим его для других типов ошибок (например, таймаут).
                throw new Exception("Save operation failed verification and timed out. Product will be deleted.");
            }

            Log.Print($"Atomic operation for '{product.Title}' completed successfully.");
            isInEditorMode = false; // Сбрасываем флаг до выхода
            return true;
        }
        catch (ValidationException valEx)
        {
            Log.Error($"--- VALIDATION FAILURE for '{product.Title}'. The product data is invalid and has been marked. ---");
            Log.Error($"Reason: {valEx.Message}");
            // Не нужно ничего удалять, так как мы еще не нажали "Сохранить".
            // Нужно безопасно выйти из редактора.
            await OpenEditorPage(Page!); // Просто перезагружаем страницу, чтобы сбросить форму
            return false; // Возвращаем false, чтобы главный цикл знал о неудаче
        }
        catch (Exception ex)
        {
            Log.Error($"CRITICAL FAILURE within single product processing for '{product.Title}'. Activating cleanup.");
            Log.Error($"Root cause: {ex.Message}");
            if (isInEditorMode)
            {
                await TryDeleteProductAsync();
            }
            else if (ex.Message.Contains("Target page, context or browser has been closed"))
            {
                throw;
            }
            return false;
        }
    }

    // --- Core Action Methods ---

    private async Task FillMainDetailsAsync(BaseProduct product)
    {
        Log.Print("Filling main details...");
        string title = product.Title.Length > BaseProduct.MaxTitleLength
            ? product.Title[..BaseProduct.MaxTitleLength]
            : product.Title;

        await Page!.Locator(XPath.TitleInput).FillAsync(title);

        await GenerateValidUrlSlugAsync(product.TranslitedTitle);

        await Page!.Locator(XPath.PriceInput).FillAsync(product.Price.ToString());
        await Page!.Locator(XPath.CountInput).FillAsync(product.Count.ToString());
        Log.Print("Finished filling main details.");
    }

    private async Task GenerateValidUrlSlugAsync(string baseSlug)
    {
        Log.Print("Generating and validating URL slug...");
        if (baseSlug.Length > MaxUrlLength)
        {
            baseSlug = baseSlug[..MaxUrlLength];
        }

        var urlInput = Page!.Locator(XPath.UrlInput);
        string currentSlug = baseSlug;
        uint attempt = 2;

        while (true)
        {
            await urlInput.ClearAsync();
            await urlInput.FillAsync(currentSlug);

            string? classes;
            do
            {
                classes = await urlInput.GetAttributeAsync("class");
            } while (classes == null || classes.Contains(CssClasses.Invalid.UrlEmpty));

            await Page.WaitForTimeoutAsync(1000);

            classes = await urlInput.GetAttributeAsync("class");
            Log.Print(classes ?? "| Could not get classes for URL input. Assuming valid and continuing.");
            if (classes == null)
            {
                Log.Error("Could not get classes for URL input. Assuming valid and continuing.");
                break;
            }
            else if (classes.Contains(CssClasses.Invalid.UrlEmpty) || !classes.Contains(CssClasses.Valid.UrlNotEmpty))
            {
                Log.Warning("URL slug is empty. Refilling...");
                continue;
            }

            if (classes.Contains(CssClasses.Invalid.UrlExists))
            {
                Log.Warning($"URL slug '{currentSlug}' already exists. Generating a new one...");
                string suffix = $"-{attempt++}";
                int availableLength = MaxUrlLength - suffix.Length;
                string trimmedBase = baseSlug.Length > availableLength ? baseSlug[..availableLength] : baseSlug;
                currentSlug = trimmedBase + suffix;
            }
            else if (classes.Contains(CssClasses.Invalid.UrlTooLong))
            {
                Log.Warning($"URL slug '{currentSlug}' is too long. Generating a new one...");
                currentSlug = baseSlug[..MaxUrlLength];
                continue;
            }
            else
            {
                Log.Print($"URL slug '{currentSlug}' ({currentSlug.Length}) is considered valid.");
                break;
            }
        }
    }

    private async Task FillDescriptionsAsync(BaseProduct product)
    {
        // Short Description
        if (!string.IsNullOrWhiteSpace(product.ShortDescription))
        {
            await NavigateToEditorTabAsync(XPath.ShortDescription.TabName);
            Log.Print("Locating iframe for short description...");
            var container = Page!.Locator(XPath.ShortDescription.Container);
            var frameLocator = container.FrameLocator(XPath.ShortDescription.Iframe);
            var editableBody = frameLocator.Locator("body");
            await editableBody.WaitForAsync();

            Log.Print("Filling short description...");
            await editableBody.FillAsync(product.ShortDescription);
            Log.Print("Short description filled.");
        }
        else
        {
            Log.Warning($"Short description is empty for product '{product.Title}'. Trying to regenerate using AI...");
            if (await product.EnqueueGenerateAiDataAsync())
            {
                Log.Print("Waiting for AI regeneration to complete...");
                await GeminiBatchProcessor.WaitForIdleAsync();
                Log.Print("AI regeneration completed.");

                product.UnmarkAsInvalid();
                await FillDescriptionsAsync(product);
                return;
            }
        }

        // Full Description
        if (!string.IsNullOrWhiteSpace(product.Description))
        {
            await NavigateToEditorTabAsync(XPath.FullDescription.TabName);
            Log.Print("Locating iframe for full description...");
            var container = Page!.Locator(XPath.FullDescription.Container);
            var frameLocator = container.FrameLocator(XPath.FullDescription.Iframe);
            var editableBody = frameLocator.Locator("body");
            await editableBody.WaitForAsync();

            Log.Print("Filling full description with attributes...");
            await editableBody.FillAsync(product.Description);

            var attributes = product.GetAttributesAsString();
            if (!string.IsNullOrWhiteSpace(attributes))
            {
                await editableBody.PressAsync("End");
                await editableBody.PressAsync("Enter");
                await editableBody.PressAsync("Enter");
                await editableBody.PressSequentiallyAsync("Характеристики", new() { Delay = 3f });

                try
                {
                    await editableBody.PressAsync("Shift+Home");
                    var boldButton = container.Locator(XPath.FullDescription.BoldButton);
                    await boldButton.ClickAsync();
                    await editableBody.PressAsync("End"); // Unselect and move to end
                    await Task.Delay(250);
                    await boldButton.ClickAsync(); // Deactivate bold for next input
                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Could not apply bold style. Non-critical error: {ex.Message}");
                }

                await editableBody.PressAsync("Enter");
                await editableBody.PressAsync("Enter");
                await editableBody.PressSequentiallyAsync(attributes, new() { Delay = 0.75f });
                Log.Print("Attributes text filled.");
            }
            Log.Print("Full description filled.");
        }
        else
        {
            Log.Warning($"Full description is empty for product '{product.Title}'. Skipping...");
        }
    }

    private async Task FillSeoDataAsync(BaseProduct product)
    {
        if (product.SEO == null)
        {
            Log.Warning($"SEO data is empty for product '{product.Title}'. Trying to regenerate using AI...");
            if (await product.EnqueueGenerateAiDataAsync())
            {
                Log.Print("Waiting for AI regeneration to complete...");
                await GeminiBatchProcessor.WaitForIdleAsync();
                Log.Print("AI regeneration completed.");

                product.UnmarkAsInvalid();
                await FillSeoDataAsync(product);
            }
            return;
        }

        await NavigateToEditorTabAsync(XPath.Seo.TabName);
        Log.Print("Filling SEO data...");

        string seoTitle = product.SEO.Title.Length > SEOProductInfo.MaxTitleLength
            ? product.SEO.Title[..SEOProductInfo.MaxTitleLength] : product.SEO.Title;

        string seoSentence = product.SEO.SeoSentence.Length > SEOProductInfo.MaxSeoSentenceLength
            ? product.SEO.SeoSentence[..SEOProductInfo.MaxSeoSentenceLength] : product.SEO.SeoSentence;

        string seoKeywords = product.SEO.Keywords.Length > SEOProductInfo.MaxKeywordsLength
            ? product.SEO.Keywords[..SEOProductInfo.MaxKeywordsLength] : product.SEO.Keywords;

        await Page!.Locator(XPath.Seo.TitleInput).FillAsync(seoTitle);
        await Page!.Locator(XPath.Seo.DescriptionInput).FillAsync(seoSentence);
        await Page!.Locator(XPath.Seo.KeywordsInput).FillAsync(seoKeywords);

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

        await _executor!.ExecuteAsync("Upload Preview Image",
            () => UploadSingleImage(XPath.PreviewImageEditor.Container, validImages[0], altText));

        for (int i = 1; i < validImages.Count; i++)
        {
            try
            {
                var validImage = validImages.ElementAtOrDefault(i);
                if (validImage == null) break;

                // For detailed images, the target container is the general photo area
                await _executor.ExecuteAsync($"Upload Detailed Image #{i}/{validImages.Count - 1}",
                    () => UploadSingleImage(XPath.DetailedImageEditor.Container, validImage, altText, isDetailed: true));
            }
            catch (OperationExecutor.ExceedImagesLimitException ex)
            {
                Log.Warning(ex.Message);
                Log.Warning("Maximum image limit reached. Stopping further image uploads for this product.");
                break;
            }
        }
    }

    private async Task UploadSingleImage(string containerXPath, string imagePath, string altText, bool isDetailed = false)
    {
        Log.Print($"Uploading image '{Path.GetFileName(imagePath)}'...");
        var container = Page!.Locator(containerXPath);

        // For detailed photos, we find the last "empty" editor slot. For preview, it's the container itself.
        var imageEditorSlot = isDetailed ? container.Locator(XPath.DetailedImageEditor.RawImageEditorInstance).Last : container;
        var uploadButton = imageEditorSlot.Locator(isDetailed ? XPath.DetailedImageEditor.UploadButton : XPath.PreviewImageEditor.UploadButton);

        await uploadButton.DispatchEventAsync("click");

        var imageWindow = Page.Locator(XPath.ImageWindowEditor.Container);
        await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var selectInput = imageWindow.Locator(XPath.ImageWindowEditor.SelectInput);
        await selectInput.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await selectInput.SetInputFilesAsync(imagePath);

        if (!isDetailed)
        {
            await imageWindow.Locator(XPath.ImageWindowEditor.SaveButton).ClickAsync(new() { Timeout = 180_000 });
        }

        await imageWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 180_000 });
        Log.Print($"Image '{Path.GetFileName(imagePath)}' uploaded.");

        // Set Alt Text
        // For detailed images, we need to find the specific editor instance that was just filled
        var filledImageEditor = isDetailed
            ? Page.Locator(XPath.DetailedImageEditor.FilledImageEditorInstance).Last
            : container;

        await filledImageEditor.Locator(XPath.PreviewImageEditor.AltButton).DispatchEventAsync("click");

        var altWindow = Page.Locator(XPath.AltWindowEditor.Container);
        await altWindow.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await altWindow.Locator(XPath.AltWindowEditor.Input).FillAsync(altText);
        await altWindow.Locator(XPath.AltWindowEditor.SaveButton).ClickAsync();
        await altWindow.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Log.Print("Alt text set.");
    }

    // --- Helper and Verification Methods ---

    private async Task<bool> AttemptSaveAndVerifyAsync(BaseProduct product)
    {
        Log.Print($"Attempting final save for '{product.Title}'...");

        var timeout = TimeSpan.FromMilliseconds(120_000);

        // --- ПРОВЕРКА ПЕРЕД СОХРАНЕНИЕМ ---
        var validationErrors = await RunValidationChecksAsync();
        if (validationErrors.Count != 0)
        {
            await product.MarkAsInvalidAsync(validationErrors);
            if (validationErrors.Any(x => x.Category == ValidationErrorCategory.RequiresAiRegeneration))
            {
                Log.Print("Trying to regenerate using AI...");
                if (await product.EnqueueGenerateAiDataAsync())
                {
                    Log.Print("Waiting for AI regeneration to complete...");
                    await GeminiBatchProcessor.WaitForIdleAsync();
                    Log.Print("AI regeneration completed.");

                    product.UnmarkAsInvalid();
                }
            }
            return false;
        }

        if (timeout.TotalMilliseconds == 0)
        {
            Log.Print("Product will be recreated. Skipping save.");
            return false;
        }

        await Page!.Locator(XPath.SaveProductButton).ClickAsync();

        try
        {
            await Page.Locator(XPath.SaveProductProcessingButton).WaitForAsync(new() { Timeout = 25_000 });
            Log.Print("Save is in progress (spinner detected). Now waiting for completion.");
        }
        catch (TimeoutException)
        {
            // NEW: If spinner doesn't appear, run validation checks to find out why.
            Log.Error("Save button did not enter 'processing' state. This often indicates a validation error.");
            await RunValidationChecksAsync();
            timeout = TimeSpan.FromMilliseconds(40_000); // Reduce wait time as it's likely stuck.
            Log.Warning($"Reduced final wait time to {timeout.TotalSeconds} seconds.");
        }

        try
        {
            await Page.Locator(XPath.SearchInCatalog).WaitForAsync(new() { Timeout = (float)timeout.TotalMilliseconds });
            Log.Print("Product saved successfully, returned to the main editor page.");
            return true;
        }
        catch (TimeoutException)
        {
            Log.Error("Failed to return to the main editor page after clicking save. The operation timed out. Declaring failure.");
            return false;
        }
    }

    /// <summary>
    /// Checks key input fields for validation errors, specifically for exceeding max length.
    /// </summary>
    private async Task<List<ValidationError>> RunValidationChecksAsync()
    {
        Log.Print("--- Running Pre-Save Validation Checks ---");
        var errors = new List<ValidationError>();

        // Определяем поля, их селекторы, лимиты и категории ошибок
        var checks = new ValidationCheck[]
        {
            new("Product Title", XPath.TitleInput, BaseProduct.MaxTitleLength, ValidationErrorCategory.RequiresManualFix),
            new("SEO Title", XPath.Seo.TitleInput, SEOProductInfo.MaxTitleLength, ValidationErrorCategory.RequiresAiRegeneration),
            new("SEO Description", XPath.Seo.DescriptionInput, SEOProductInfo.MaxSeoSentenceLength, ValidationErrorCategory.RequiresAiRegeneration),
            new("SEO Keywords", XPath.Seo.KeywordsInput, SEOProductInfo.MaxKeywordsLength, ValidationErrorCategory.RequiresAiRegeneration)
        };

        foreach (var check in checks)
        {
            try
            {
                var input = Page!.Locator(check.Selector);
                string? classes = await input.GetAttributeAsync("class", new() { Timeout = 1000 });
                if (classes != null && classes.Contains(CssClasses.Invalid.MaxLength))
                {
                    string value = await input.InputValueAsync();
                    Log.Error($"VALIDATION FAILED: The '{check.Name}' field has an invalid length ({value.Length}/{check.Limit}).");
                    errors.Add(new ValidationError(
                        FieldName: check.Name,
                        ErrorType: "MaxLengthExceeded",
                        OffendingValue: value,
                        Limit: check.Limit,
                        Category: check.Category
                    ));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not perform validation check for '{check.Name}': {ex.Message}");
            }
        }

        try
        {
            string fieldName = "Short Description";
            var container = Page!.Locator(XPath.ShortDescription.Container);
            var validatorTextarea = container.Locator(XPath.ShortDescription.TextareaValidator);

            string? classes = await validatorTextarea.GetAttributeAsync("class", new() { Timeout = 1000 });
            if (classes != null && classes.Contains(CssClasses.Invalid.MaxLengthNg))
            {
                // Получаем значение не из textarea, а из тела iframe
                var frame = container.FrameLocator(XPath.ShortDescription.Iframe);
                var body = frame.Locator("body");
                string value = await body.TextContentAsync() ?? string.Empty;
                int limit = BaseProduct.MaxShortDescriptionLength;

                Log.Error($"VALIDATION FAILED: The '{fieldName}' field has an invalid length ({value.Length}/{limit}).");
                errors.Add(new ValidationError(fieldName, "MaxLengthExceeded", value, limit, ValidationErrorCategory.RequiresAiRegeneration));
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Could not perform validation check for 'Short Description': {ex.Message}");
        }

        Log.Print("--- Finished Validation Checks ---");
        return errors;
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
                await OpenEditorPage(Page);
                return false;
            }

            Page.Dialog += async (_, dialog) =>
            {
                Log.Warning($"Browser dialog opened with message: '{dialog.Message}'. Accepting...");
                await dialog.AcceptAsync();
            };

            await deleteButton.ClickAsync();
            await Page.Locator(XPath.AddProductButton).WaitForAsync(new() { Timeout = 30000 });
            Log.Print("Emergency delete successful. Unsaved product draft has been removed.");
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
        Log.Print($"Navigating to '{tabName}' tab...");
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
                if (!File.Exists(path)) { Log.Warning($"Image not found, skipping: {path}"); continue; }

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > MaxImageSizeInBytes) { Log.Warning($"Image too large ({fileInfo.Length / 1024} KB), skipping: {path}"); continue; }

                var dimensions = ImageUtils.GetImageDimensions(path);
                if (!ImageUtils.IsResolutionInRange(dimensions, MinImageSize, MaxImageSize)) { Log.Warning($"Image dimensions out of range ({dimensions.X}x{dimensions.Y}), skipping: {path}"); continue; }

                validImages.Add(path);
            }
            catch (Exception ex)
            {
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
        Page = loadedPage;
        Log.Print("Page opened. Enabling editor...");

        await Page.Locator(XPath.EnableEditorButton).First.DispatchEventAsync("click");

        Log.Print("Waiting for main editor interface to load...");
        await Page.Locator(XPath.AddProductButton).WaitForAsync(new() { Timeout = 20000, State = WaitForSelectorState.Attached });
        Log.Print("Editor interface loaded successfully.");
        return true;
    }

    private sealed record class ValidationCheck(in string Name, in string Selector, in int Limit, in ValidationErrorCategory Category);

    /// <summary>
    /// Исключение, выбрасываемое, когда данные продукта не проходят внутреннюю валидацию.
    /// </summary>
    private sealed class ValidationException(string message) : Exception(message);

}