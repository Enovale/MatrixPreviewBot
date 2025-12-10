namespace MatrixPreviewBot.Extensions;
 
public static class DictionaryExtensions
{
    // One-line extension method to return value or null/default
    public static TValue? ValueOrNull<TKey, TValue>(
        this IDictionary<TKey, TValue>? dictionary, 
        TKey key
    ) where TKey : notnull 
        => dictionary?.TryGetValue(key, out var value) == true ? value : default;
}