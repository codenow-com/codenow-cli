using System.Text.Json;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.Common.Security;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Json;

public class EncryptedStringJsonConverterTests
{
    private sealed class SecretPayload
    {
        public string? Value { get; set; }
    }

    [Fact]
    public void Converter_RoundTripsEncryptedValues()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new EncryptedStringJsonConverter(() => "passphrase"));

        var payload = new SecretPayload { Value = "top-secret" };
        var json = JsonSerializer.Serialize(payload, options);

        Assert.Contains(SecretProtector.Prefix, json);

        var roundTrip = JsonSerializer.Deserialize<SecretPayload>(json, options);

        Assert.Equal("top-secret", roundTrip!.Value);
    }

    [Fact]
    public void Converter_LeavesEmptyStringsUnchanged()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new EncryptedStringJsonConverter(() => "passphrase"));

        var payload = new SecretPayload { Value = string.Empty };
        var json = JsonSerializer.Serialize(payload, options);

        Assert.Contains("\"Value\":\"\"", json);

        var roundTrip = JsonSerializer.Deserialize<SecretPayload>(json, options);
        Assert.Equal(string.Empty, roundTrip!.Value);
    }

    [Fact]
    public void Converter_AllowsNullValues()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new EncryptedStringJsonConverter(() => "passphrase"));

        var payload = new SecretPayload { Value = null };
        var json = JsonSerializer.Serialize(payload, options);

        Assert.Contains("\"Value\":null", json);

        var roundTrip = JsonSerializer.Deserialize<SecretPayload>(json, options);
        Assert.Null(roundTrip!.Value);
    }
}
