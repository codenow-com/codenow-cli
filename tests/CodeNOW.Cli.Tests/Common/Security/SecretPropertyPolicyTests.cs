using CodeNOW.Cli.Common.Security;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Security;

public class SecretPropertyPolicyTests
{
    [Theory]
    [InlineData("Username")]
    [InlineData("Password")]
    [InlineData("AccessToken")]
    [InlineData("AccessKey")]
    [InlineData("SecretKey")]
    [InlineData("Passphrase")]
    public void IsEncrypted_ReturnsTrueForSensitiveNames(string propertyName)
    {
        Assert.True(SecretPropertyPolicy.IsEncrypted(propertyName));
    }

    [Fact]
    public void IsEncrypted_ReturnsFalseForOtherNames()
    {
        Assert.False(SecretPropertyPolicy.IsEncrypted("Other"));
    }

    [Fact]
    public void IsEncrypted_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => SecretPropertyPolicy.IsEncrypted(null!));
    }
}
