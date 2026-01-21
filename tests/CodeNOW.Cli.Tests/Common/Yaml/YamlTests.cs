using System.Text.Json;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Common.Yaml;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Yaml;

public class YamlTests
{
    [Fact]
    public void YamlNormalizer_Normalize_ConvertsNestedStructures()
    {
        var input = new Dictionary<object, object>
        {
            ["name"] = "demo",
            ["count"] = 3,
            ["nested"] = new Dictionary<object, object>
            {
                ["flag"] = true,
                ["items"] = new List<object> { "one", 2 }
            }
        };

        var normalized = YamlNormalizer.Normalize(input);

        var root = Assert.IsType<Dictionary<string, object?>>(normalized);
        Assert.Equal("demo", root["name"]);
        Assert.Equal(3, root["count"]);

        var nested = Assert.IsType<Dictionary<string, object?>>(root["nested"]);
        Assert.Equal(true, nested["flag"]);

        var items = Assert.IsType<List<object?>>(nested["items"]);
        Assert.Equal("one", items[0]);
        Assert.Equal(2, items[1]);
    }

    [Fact]
    public void YamlToJsonConverter_ConvertAll_ParsesMultipleDocuments()
    {
        var yaml = """
        a: 1
        b: true
        c: "001"
        ---
        name: sample
        nested:
          value: 42
        """;

        var converter = new YamlToJsonConverter();
        var docs = converter.ConvertAll(yaml).ToList();

        Assert.Equal(2, docs.Count);

        var first = docs[0];
        Assert.Equal(1, first["a"]!.GetValue<int>());
        Assert.True(first["b"]!.GetValue<bool>());
        Assert.Equal("001", first["c"]!.GetValue<string>());

        var second = docs[1];
        Assert.Equal("sample", second["name"]!.GetValue<string>());
        var nested = second["nested"]!.AsObject();
        Assert.Equal(42, nested["value"]!.GetValue<int>());
    }

    [Fact]
    public void YamlToJsonConverter_ConvertAll_RespectsQuotedScalars()
    {
        var yaml = """
        numeric_string: "123"
        plain_number: 123
        """;

        var converter = new YamlToJsonConverter();
        var doc = converter.ConvertAll(yaml).Single();

        Assert.Equal("123", doc["numeric_string"]!.GetValue<string>());
        Assert.Equal(123, doc["plain_number"]!.GetValue<int>());
    }

    [Fact]
    public void YamlJsonContext_SerializesNormalizedObjects()
    {
        var normalized = new Dictionary<string, object?>
        {
            ["name"] = "demo",
            ["enabled"] = true,
            ["count"] = 5
        };

        var json = JsonSerializer.Serialize(normalized, YamlJsonContext.Default.Object);
        var parsed = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("demo", parsed["name"]!.GetValue<string>());
        Assert.True(parsed["enabled"]!.GetValue<bool>());
        Assert.Equal(5, parsed["count"]!.GetValue<int>());
    }
}
