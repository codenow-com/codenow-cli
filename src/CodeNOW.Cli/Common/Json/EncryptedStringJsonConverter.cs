using System.Text.Json;
using System.Text.Json.Serialization;
using CodeNOW.Cli.Common.Security;

namespace CodeNOW.Cli.Common.Json;

/// <summary>
/// JSON converter that transparently encrypts and decrypts string values.
/// </summary>
internal sealed class EncryptedStringJsonConverter : JsonConverter<string>
{
    private readonly Func<string> _getSecretKey;

    /// <summary>
    /// Creates a new instance with a secret key provider.
    /// </summary>
    /// <param name="getSecretKey">Function that returns the current secret key.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="getSecretKey"/> is null.</exception>
    public EncryptedStringJsonConverter(Func<string> getSecretKey)
    {
        if (getSecretKey is null)
            throw new ArgumentNullException(nameof(getSecretKey));

        _getSecretKey = getSecretKey;
    }

    /// <inheritdoc />
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // Preserve nulls and empty strings without touching the encryption flow.
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return value;

        // Transparently decrypt values that use the supported prefix.
        return SecretProtector.DecryptIfEncrypted(value, _getSecretKey());
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        // Keep empty strings as-is; encryption is only for real values.
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteStringValue(value);
            return;
        }

        // Always encrypt non-empty values on write.
        writer.WriteStringValue(
            SecretProtector.EncryptToString(value, _getSecretKey()));
    }
}
