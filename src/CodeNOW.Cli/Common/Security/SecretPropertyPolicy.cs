namespace CodeNOW.Cli.Common.Security;

/// <summary>
/// Central definition of configuration property names
/// that must be encrypted when serialized.
/// </summary>
public static class SecretPropertyPolicy
{
    /// <summary>
    /// Property names that should be encrypted when serialized.
    /// </summary>
    private static readonly HashSet<string> EncryptedPropertyNames =
        new(StringComparer.Ordinal)
        {
            "Username",
            "Password",
            "AccessToken",
            "AccessKey",
            "SecretKey",
            "Passphrase"
        };

    /// <summary>
    /// Returns true when a property name is configured as encrypted.
    /// </summary>
    /// <param name="propertyName">Property name to check.</param>
    /// <returns>True if the property is encrypted; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="propertyName"/> is null.</exception>
    public static bool IsEncrypted(string propertyName)
        => EncryptedPropertyNames.Contains(
            propertyName ?? throw new ArgumentNullException(nameof(propertyName)));
}
