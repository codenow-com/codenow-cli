using System.Security.Cryptography;
using System.Text.Json;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Serialization;

namespace CodeNOW.Cli.DataPlane.Console.Supports;

/// <summary>
/// Loads and saves encrypted operator configuration files.
/// </summary>
internal sealed class BootstrapConfigStore
{
    /// <summary>
    /// Loads an operator configuration from disk.
    /// </summary>
    public OperatorConfig LoadConfig(string filePath, string encryptionKey)
    {
        var typeInfo = OperatorConfigJsonTypeInfoFactory.Create(() => encryptionKey);
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize(json, typeInfo)
            ?? throw new InvalidOperationException("Failed to deserialize configuration.");
    }

    /// <summary>
    /// Saves the operator configuration to disk.
    /// </summary>
    public void SaveConfig(string outputPath, OperatorConfig config, string encryptionKey)
    {
        var typeInfo = OperatorConfigJsonTypeInfoFactory.Create(() => encryptionKey);
        using var stream = File.Create(outputPath);
        using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Indented = true
            });
        JsonSerializer.Serialize(writer, config, typeInfo);
    }

    /// <summary>
    /// Generates a random base64 encryption key.
    /// </summary>
    public string GenerateEncryptionKey()
    {
        Span<byte> buffer = stackalloc byte[32]; // 256-bit
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    /// Generates a random base64 Pulumi passphrase.
    /// </summary>
    public string GeneratePulumiPassphrase()
    {
        Span<byte> buffer = stackalloc byte[32]; // 256-bit
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }
}
