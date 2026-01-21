using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeNOW.Cli.Common.Json;

/// <summary>
/// Utility extensions for setting and retrieving values in <see cref="JsonObject"/> instances using dotted paths with optional array indices; kept simple and reflection-free to stay AOT friendly.
/// </summary>
internal static class JsonPatchExtensions
{
    private static readonly Regex ArrayRegex = new(@"^(\w+)\[(\d+)\]$", RegexOptions.Compiled);

    /// <summary>
    /// Sets a value on a <see cref="JsonObject"/> using a dotted path with optional array indices, e.g.:
    /// metadata.namespace
    /// spec.template.spec.imagePullSecrets[0].name
    /// subjects[1].namespace
    /// </summary>
    public static void Set(this JsonObject obj, string path, string value)
        => SetNode(obj, path, JsonValue.Create(value)!);

    /// <summary>
    /// Sets a boolean value using a dotted path with optional array indices.
    /// </summary>
    public static void Set(this JsonObject obj, string path, bool value)
        => SetNode(obj, path, JsonValue.Create(value)!);

    /// <summary>
    /// Sets an integer value using a dotted path with optional array indices.
    /// </summary>
    public static void Set(this JsonObject obj, string path, int value)
        => SetNode(obj, path, JsonValue.Create(value)!);

    /// <summary>
    /// Sets a <see cref="JsonNode"/> value using a dotted path with optional array indices.
    /// </summary>
    public static void Set(this JsonObject obj, string path, JsonNode value)
        => SetNode(obj, path, value);

    private static void SetNode(JsonObject obj, string path, JsonNode value)
    {
        var parts = path.Split('.');
        JsonNode current = obj;

        for (int i = 0; i < parts.Length; i++)
        {
            bool isLast = i == parts.Length - 1;
            string part = parts[i];

            if (TryParseArray(part, out var key, out var index))
            {
                current = EnsureArrayNode(current, key, index, isLast ? value : null);
                continue;
            }

            current = EnsureObjectNode(current, part, isLast ? value : null);
        }
    }

    /// <summary>
    /// Tries to parse a path segment of the form <c>key[index]</c> into its components.
    /// </summary>
    internal static bool TryParseArray(string part, out string key, out int index)
    {
        key = "";
        index = -1;

        var match = ArrayRegex.Match(part);
        if (!match.Success)
            return false;

        key = match.Groups[1].Value;
        index = int.Parse(match.Groups[2].Value);
        return true;
    }

    /// <summary>
    /// Attempts to resolve a string value using a dotted path with optional array indices.
    /// </summary>
    public static bool TryGetString(this JsonObject obj, string path, out string value)
    {
        value = "";
        var parts = path.Split('.');
        JsonNode? current = obj;
        foreach (var part in parts)
        {
            if (TryParseArray(part, out var key, out var index))
            {
                current = (current as JsonObject)?[key];
                current = current is JsonArray arr && index < arr.Count ? arr[index] : null;
            }
            else
            {
                current = (current as JsonObject)?[part];
            }
        }

        if (current is JsonValue val && val.TryGetValue<string>(out var s))
        {
            value = s;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retrieves a required string value from the provided path or throws if the value is missing or empty.
    /// </summary>
    public static string GetRequiredString(this JsonObject obj, string path)
    {
        if (TryGetString(obj, path, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"Required value '{path}' not found in manifest.");
    }

    /// <summary>
    /// Ensures a nested <see cref="JsonObject"/> exists at the provided key, creating or setting values as needed.
    /// </summary>
    private static JsonNode EnsureObjectNode(JsonNode current, string key, JsonNode? value)
    {
        var obj = current as JsonObject ?? throw new InvalidOperationException("Current node is not an object.");

        if (value != null)
        {
            obj[key] = value;
            return obj[key]!;
        }

        if (obj[key] is not JsonObject nextObj)
        {
            nextObj = new JsonObject();
            obj[key] = nextObj;
        }

        return nextObj;
    }

    /// <summary>
    /// Ensures a <see cref="JsonArray"/> and optional nested <see cref="JsonObject"/> exist at the given key and index.
    /// </summary>
    private static JsonNode EnsureArrayNode(JsonNode current, string key, int index, JsonNode? value)
    {
        var obj = current as JsonObject ?? throw new InvalidOperationException("Current node is not an object.");

        if (obj[key] is not JsonArray array)
        {
            array = new JsonArray();
            obj[key] = array;
        }

        while (array.Count <= index)
        {
            array.Add((JsonNode?)null);
        }

        if (value != null)
        {
            array[index] = value;
            return array[index]!;
        }

        if (array[index] is not JsonObject nextObj)
        {
            nextObj = new JsonObject();
            array[index] = nextObj;
        }

        return nextObj;
    }
}
