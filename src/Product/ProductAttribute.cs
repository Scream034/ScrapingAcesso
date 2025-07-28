namespace ScraperAcesso.Product
{
    /// <summary>
    /// Represents a single characteristic (attribute) of a product.
    /// </summary>
    /// <param name="Name">The name of the attribute (e.g., "Color", "Material").</param>
    /// <param name="Value">The value of the attribute (e.g., "Red", "Cotton").</param>
    public sealed record ProductAttribute(string Name, string Value);
}