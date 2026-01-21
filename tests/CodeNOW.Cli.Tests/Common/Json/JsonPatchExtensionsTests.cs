using System.Text.Json.Nodes;
using CodeNOW.Cli.Common.Json;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Json;

public class JsonPatchExtensionsTests
{
    [Fact]
    public void Set_CreatesNestedObjectsAndArrays()
    {
        var root = new JsonObject();

        root.Set("spec.template.spec.imagePullSecrets[0].name", "secret");

        var name = root["spec"]!.AsObject()
            ["template"]!.AsObject()
            ["spec"]!.AsObject()
            ["imagePullSecrets"]!.AsArray()
            [0]!.AsObject()
            ["name"]!.GetValue<string>();

        Assert.Equal("secret", name);
    }

    [Fact]
    public void Set_SetsPrimitiveValues()
    {
        var root = new JsonObject();

        root.Set("spec.enabled", true);
        root.Set("spec.replicas", 3);

        Assert.True(root["spec"]!.AsObject()["enabled"]!.GetValue<bool>());
        Assert.Equal(3, root["spec"]!.AsObject()["replicas"]!.GetValue<int>());
    }

    [Fact]
    public void TryGetString_ReadsArrayIndexedValue()
    {
        var root = new JsonObject
        {
            ["items"] = new JsonArray
            {
                new JsonObject { ["name"] = "first" },
                new JsonObject { ["name"] = "second" }
            }
        };

        var found = root.TryGetString("items[1].name", out var value);

        Assert.True(found);
        Assert.Equal("second", value);
    }

    [Fact]
    public void GetRequiredString_ThrowsWhenMissing()
    {
        var root = new JsonObject();

        Assert.Throws<InvalidOperationException>(() => root.GetRequiredString("missing.value"));
    }

    [Fact]
    public void TryParseArray_ParsesValidSegment()
    {
        var ok = JsonPatchExtensions.TryParseArray("items[2]", out var key, out var index);

        Assert.True(ok);
        Assert.Equal("items", key);
        Assert.Equal(2, index);
    }
}
