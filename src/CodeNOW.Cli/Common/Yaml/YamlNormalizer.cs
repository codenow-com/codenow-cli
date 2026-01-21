namespace CodeNOW.Cli.Common.Yaml;

/// <summary>
/// Normalizes YamlDotNet objects into plain .NET collections with string keys for consistent JSON serialization while avoiding reflection to support AOT.
/// </summary>
internal static class YamlNormalizer
{
    /// <summary>
    /// Recursively converts YamlDotNet mapping and sequence nodes into string-keyed dictionaries and lists.
    /// </summary>
    public static object Normalize(object obj)
    {
        return obj switch
        {
            Dictionary<object, object> dict =>
                dict.ToDictionary(
                    kv => kv.Key.ToString()!,
                    kv => Normalize(kv.Value)
                ),

            List<object> list =>
                list.Select(Normalize).ToList(),

            _ => obj
        };
    }
}
