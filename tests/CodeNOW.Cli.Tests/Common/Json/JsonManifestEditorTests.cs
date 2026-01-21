using System.Text.Json.Nodes;
using CodeNOW.Cli.Common.Json;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Json;

public class JsonManifestEditorTests
{
    [Fact]
    public void EnsureObjectPath_CreatesNestedObjects()
    {
        var root = new JsonObject();

        var nested = JsonManifestEditor.EnsureObjectPath(root, "spec.template.metadata");

        Assert.NotNull(nested);
        Assert.NotNull(root["spec"]!.AsObject()["template"]!.AsObject()["metadata"]);
    }

    [Fact]
    public void EnsureArrayPath_CreatesArrayAtPath()
    {
        var root = new JsonObject();

        var array = JsonManifestEditor.EnsureArrayPath(root, "spec.containers");

        Assert.Same(array, root["spec"]!.AsObject()["containers"]);
        Assert.Empty(array);
    }

    [Fact]
    public void EnsureArray_CreatesArrayProperty()
    {
        var root = new JsonObject();

        var array = JsonManifestEditor.EnsureArray(root, "items");

        Assert.Same(array, root["items"]);
    }

    [Fact]
    public void FindByName_ReturnsMatchingObject()
    {
        var array = new JsonArray
        {
            new JsonObject { ["name"] = "first" },
            new JsonObject { ["name"] = "second" }
        };

        var found = JsonManifestEditor.FindByName(array, "second");

        Assert.NotNull(found);
        Assert.Equal("second", found!["name"]!.GetValue<string>());
    }

    [Fact]
    public void EnsureNamedObject_AddsWhenMissing()
    {
        var array = new JsonArray();

        var created = JsonManifestEditor.EnsureNamedObject(array, "item");

        Assert.Single(array);
        Assert.Same(created, array[0]);
    }

    [Fact]
    public void EnsureEnvVar_AddsOnlyOnce()
    {
        var array = new JsonArray();

        JsonManifestEditor.EnsureEnvVar(array, "HTTP_PROXY", "localhost");
        JsonManifestEditor.EnsureEnvVar(array, "HTTP_PROXY", "localhost");

        Assert.Single(array);
        Assert.Equal("HTTP_PROXY", array[0]!.AsObject()["name"]!.GetValue<string>());
    }
}
