namespace ScraperAcesso.Product
{
    /// <summary>
    /// Категория ошибки, определяющая дальнейшие действия.
    /// </summary>
    public enum ValidationErrorCategory
    {
        /// <summary>
        /// Требуется ручное исправление данных.
        /// </summary>
        RequiresManualFix,

        /// <summary>
        /// Требуется перегенерация контента (например, через ИИ).
        /// </summary>
        RequiresAiRegeneration
    }

    /// <summary>
    /// Описывает одну конкретную ошибку валидации.
    /// </summary>
    /// <param name="FieldName">Понятное имя поля, где произошла ошибка.</param>
    /// <param name="ErrorType">Тип ошибки (например, "MaxLengthExceeded").</param>
    /// <param name="OffendingValue">Значение, вызвавшее ошибку.</param>
    /// <param name="Limit">Числовое ограничение, которое было нарушено.</param>
    /// <param name="Category">Предлагаемое действие для исправления.</param>
    public sealed record ValidationError(
        string FieldName,
        string ErrorType,
        string OffendingValue,
        int Limit,
        ValidationErrorCategory Category);

    /// <summary>
    /// Структура файла validation_error.json, который создается для "сломанных" товаров.
    /// </summary>
    /// <param name="ErrorTimestampUtc">Время обнаружения ошибки.</param>
    /// <param name="Errors">Список обнаруженных ошибок валидации.</param>
    public sealed record ValidationErrorFile(
        DateTime ErrorTimestampUtc,
        List<ValidationError> Errors);
}