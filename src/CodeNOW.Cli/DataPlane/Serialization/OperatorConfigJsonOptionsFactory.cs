using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.Common.Security;

namespace CodeNOW.Cli.DataPlane.Serialization;

/// <summary>
/// Creates JSON type info for <see cref="OperatorConfig"/> with encryption support.
/// </summary>
internal static class OperatorConfigJsonTypeInfoFactory
{
    /// <summary>
    /// Builds a <see cref="JsonTypeInfo{T}"/> that applies encrypted string converters
    /// to properties marked by <see cref="SecretPropertyPolicy"/>.
    /// </summary>
    /// <param name="getSecretKey">Function that returns the current secret key.</param>
    /// <returns>Type info configured for encrypted string handling.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="getSecretKey"/> is null.</exception>
    public static JsonTypeInfo<OperatorConfig> Create(Func<string> getSecretKey)
    {
        if (getSecretKey is null)
            throw new ArgumentNullException(nameof(getSecretKey));

        var encryptedConverter =
            new EncryptedStringJsonConverter(getSecretKey);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = OperatorConfigJsonContext.Default
                .WithAddedModifier(typeInfo =>
                {
                    if (typeInfo.Kind != JsonTypeInfoKind.Object)
                        return;

                    foreach (var prop in typeInfo.Properties)
                    {
                        if (prop.PropertyType == typeof(string) &&
                            SecretPropertyPolicy.IsEncrypted(prop.Name))
                        {
                            prop.CustomConverter = encryptedConverter;
                        }
                    }
                })
        };

        return (JsonTypeInfo<OperatorConfig>)
            options.GetTypeInfo(typeof(OperatorConfig));
    }
}
