using System.Text.Json.Nodes;

namespace CodeNOW.Cli.Common.Json;

/// <summary>
/// Helpers for building and editing JSON manifest structures.
/// </summary>
internal static class JsonManifestEditor
{
    /// <summary>
    /// Ensures a <see cref="JsonObject"/> exists at the dotted path and returns it.
    /// </summary>
    public static JsonObject EnsureObjectPath(JsonObject root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current[part] is not JsonObject next)
            {
                next = new JsonObject();
                current[part] = next;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Ensures a <see cref="JsonArray"/> exists at the dotted path and returns it.
    /// </summary>
    public static JsonArray EnsureArrayPath(JsonObject root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new JsonArray();

        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (current[part] is not JsonObject next)
            {
                next = new JsonObject();
                current[part] = next;
            }

            current = next;
        }

        var arrayKey = parts[^1];
        if (current[arrayKey] is not JsonArray array)
        {
            array = new JsonArray();
            current[arrayKey] = array;
        }

        return array;
    }

    /// <summary>
    /// Ensures a <see cref="JsonArray"/> exists at the given property and returns it.
    /// </summary>
    public static JsonArray EnsureArray(JsonObject root, string propertyName)
    {
        if (root[propertyName] is not JsonArray array)
        {
            array = new JsonArray();
            root[propertyName] = array;
        }

        return array;
    }

    /// <summary>
    /// Finds the first object in the array with a matching <c>name</c> property.
    /// </summary>
    public static JsonObject? FindByName(JsonArray array, string name)
    {
        foreach (var node in array)
        {
            if (node is not JsonObject obj)
                continue;

            var nodeName = obj["name"]?.GetValue<string>();
            if (string.Equals(nodeName, name, StringComparison.Ordinal))
                return obj;
        }

        return null;
    }

    /// <summary>
    /// Returns true when the array contains an object with a matching <c>name</c> property.
    /// </summary>
    public static bool HasNamedObject(JsonArray array, string name)
    {
        return FindByName(array, name) is not null;
    }

    /// <summary>
    /// Ensures an object with a matching <c>name</c> property exists and returns it.
    /// </summary>
    public static JsonObject EnsureNamedObject(JsonArray array, string name)
    {
        var existing = FindByName(array, name);
        if (existing is not null)
            return existing;

        var created = new JsonObject
        {
            ["name"] = name
        };
        array.Add((JsonNode)created);
        return created;
    }

    /// <summary>
    /// Ensures an environment variable entry exists in the array.
    /// </summary>
    public static void EnsureEnvVar(JsonArray env, string name, string value)
    {
        if (HasNamedObject(env, name))
            return;

        env.Add((JsonNode)new JsonObject
        {
            ["name"] = name,
            ["value"] = value
        });
    }
}
