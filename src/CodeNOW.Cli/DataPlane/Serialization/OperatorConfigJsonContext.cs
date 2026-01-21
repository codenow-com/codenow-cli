using System.Text.Json.Serialization;
using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Serialization;

/// <summary>
/// Source-generated JSON serialization context for operator configuration.
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OperatorConfig))]
internal partial class OperatorConfigJsonContext : JsonSerializerContext;
