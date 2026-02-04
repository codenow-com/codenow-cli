using System.Security.Cryptography;
using System.Text.Json;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Serialization;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Handles loading and encrypting operator configuration files.
/// </summary>
public sealed class OperatorConfigService
{
    /// <summary>
    /// Encrypts secret values in the operator configuration file in place.
    /// </summary>
    /// <param name="configPath">Path to the plaintext operator configuration file.</param>
    /// <returns>Generated encryption key.</returns>
    public string EncryptConfigFile(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));

        if (!File.Exists(configPath))
            throw new FileNotFoundException("Configuration file not found.", configPath);

        var encryptionKey = GenerateEncryptionKey();
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize(
            json,
            OperatorConfigJsonContext.Default.OperatorConfig)
            ?? throw new InvalidOperationException("Failed to deserialize configuration.");
        var typeInfo = OperatorConfigJsonTypeInfoFactory.Create(() => encryptionKey);

        using var stream = File.Create(configPath);
        using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = true
            });
        JsonSerializer.Serialize(writer, config, typeInfo);

        return encryptionKey;
    }

    private static string GenerateEncryptionKey()
    {
        Span<byte> buffer = stackalloc byte[32]; // 256-bit
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

}
