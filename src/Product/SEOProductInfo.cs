namespace ScraperAcesso.Product;

using System.Text.Json.Serialization;

public sealed record class SEOProductInfo(string Title, string SeoSentence, string Keywords)
{
    public const int MaxTitleLength = 256;
    public const int MaxSeoSentenceLength = 200;
    public const int MaxKeywordsLength = 100;

    [JsonPropertyName("seo_title")]
    public string Title { get; set; } = Title.Length > MaxTitleLength ? Title[..MaxTitleLength] : Title;
    [JsonPropertyName("seo_sentence")]
    public string SeoSentence { get; set; } = SeoSentence.Length > MaxSeoSentenceLength ? SeoSentence[..MaxSeoSentenceLength] : SeoSentence;
    [JsonPropertyName("keywords")]
    public string Keywords { get; set; } = Keywords.Length > MaxKeywordsLength ? Keywords[..MaxKeywordsLength] : Keywords;
}