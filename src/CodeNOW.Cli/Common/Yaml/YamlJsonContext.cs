using System.Text.Json.Serialization;

namespace CodeNOW.Cli.Common.Yaml;

/// <summary>
/// Source-generated JSON serialization context for common YAML-to-JSON intermediary types to keep runtime serialization AOT friendly.
/// </summary>
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(List<object?>))]
internal partial class YamlJsonContext : JsonSerializerContext
{
}
