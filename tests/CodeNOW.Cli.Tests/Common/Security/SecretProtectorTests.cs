using System.Security.Cryptography;
using CodeNOW.Cli.Common.Security;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Security;

public class SecretProtectorTests
{
    [Fact]
    public void EncryptToString_AddsPrefixAndDecrypts()
    {
        var encrypted = SecretProtector.EncryptToString("value", "passphrase");

        Assert.StartsWith(SecretProtector.Prefix, encrypted, StringComparison.Ordinal);

        var decrypted = SecretProtector.DecryptToString(encrypted, "passphrase");

        Assert.Equal("value", decrypted);
    }

    [Fact]
    public void EncryptIfNotEmpty_ReturnsNullOrEmptyUnchanged()
    {
        Assert.Null(SecretProtector.EncryptIfNotEmpty(null, "passphrase"));
        Assert.Equal(string.Empty, SecretProtector.EncryptIfNotEmpty(string.Empty, "passphrase"));
    }

    [Fact]
    public void DecryptIfEncrypted_ReturnsOriginalWhenNotEncrypted()
    {
        var value = SecretProtector.DecryptIfEncrypted("plain", "passphrase");

        Assert.Equal("plain", value);
    }

    [Fact]
    public void DecryptToString_ThrowsOnInvalidPrefix()
    {
        Assert.Throws<FormatException>(() => SecretProtector.DecryptToString("plain", "passphrase"));
    }

    [Fact]
    public void DecryptToString_ThrowsOnInvalidBase64()
    {
        var bad = SecretProtector.Prefix + "not-base64";

        Assert.Throws<FormatException>(() => SecretProtector.DecryptToString(bad, "passphrase"));
    }

    [Fact]
    public void EncryptToString_ThrowsOnEmptyPassphrase()
    {
        Assert.Throws<ArgumentException>(() => SecretProtector.EncryptToString("value", ""));
    }

    [Fact]
    public void DecryptToString_ThrowsOnEmptyPassphrase()
    {
        var encrypted = SecretProtector.EncryptToString("value", "passphrase");

        Assert.Throws<ArgumentException>(() => SecretProtector.DecryptToString(encrypted, ""));
    }

    [Fact]
    public void DecryptToString_ThrowsOnWrongKey()
    {
        var encrypted = SecretProtector.EncryptToString("value", "passphrase");

        Assert.Throws<CryptographicException>(() => SecretProtector.DecryptToString(encrypted, "wrong"));
    }
}
