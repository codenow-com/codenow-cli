using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CodeNOW.Cli.Common.Yaml;

/// <summary>
/// Converts YAML documents into normalized <see cref="JsonObject"/> instances for downstream processing with an AOT-friendly, source-generated serialization setup.
/// </summary>
public class YamlToJsonConverter
{
    /// <summary>
    /// Parses all YAML documents from the input text and yields normalized JSON objects.
    /// </summary>
    /// <param name="yamlText">YAML text containing one or more documents.</param>
    /// <returns>Sequence of normalized JSON objects.</returns>
    public IEnumerable<JsonObject> ConvertAll(string yamlText)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(yamlText));

        foreach (var doc in yaml.Documents)
        {
            var root = ConvertYamlNode(doc.RootNode);
            if (root == null)
                continue;

            var normalized = YamlNormalizer.Normalize(root);

            var json = JsonSerializer.Serialize(
                normalized,
                YamlJsonContext.Default.Object
            );

            yield return JsonNode.Parse(json)!.AsObject();
        }
    }

    /// <summary>
    /// Recursively converts a YamlDotNet node into plain .NET types prior to normalization.
    /// </summary>
    private static object? ConvertYamlNode(YamlNode node)
    {
        switch (node.NodeType)
        {
            case YamlNodeType.Mapping:
                var map = new Dictionary<string, object?>();
                var mapping = (YamlMappingNode)node;

                foreach (var entry in mapping.Children)
                    map[entry.Key.ToString()] = ConvertYamlNode(entry.Value);

                return map;

            case YamlNodeType.Sequence:
                var list = new List<object?>();
                var seq = (YamlSequenceNode)node;

                foreach (var child in seq.Children)
                    list.Add(ConvertYamlNode(child));

                return list;

            case YamlNodeType.Scalar:
                var scalar = (YamlScalarNode)node;

                if (scalar.Style != ScalarStyle.Plain)
                    return scalar.Value;

                if (int.TryParse(scalar.Value, out var intVal))
                    return intVal;

                if (long.TryParse(scalar.Value, out var longVal))
                    return longVal;

                if (bool.TryParse(scalar.Value, out var boolVal))
                    return boolVal;

                // return string as fallback
                return scalar.Value;

            default:
                return node.ToString();
        }
    }

}
