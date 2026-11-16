using System.Security.Cryptography;
using CodeNOW.Cli.DataPlane.Models;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class ScmConfigValidatorTests
{
    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "https://api.github.com", true)]
    [InlineData(false, "https://api.github.com/", true)]
    [InlineData(true, "https://github.example.com/api/v3", true)]
    [InlineData(false, "https://github.example.com/api/v3", false)]
    [InlineData(true, "not-a-url", false)]
    public void Validate_EnforcesEnterpriseFluxRequirement(bool flux, string? apiUrl, bool valid)
    {
        using var rsa = RSA.Create(2048);
        var config = new OperatorConfig
        {
            Scm =
            {
                AuthenticationMethod = ScmAuthenticationMethod.GitHubApp,
                GithubAppId = "123",
                GithubAppInstallationId = "456",
                GithubAppPrivateKey = rsa.ExportRSAPrivateKeyPem(),
                GithubApiUrl = apiUrl
            },
            FluxCD = new FluxCDConfig { Enabled = flux }
        };
        if (valid)
            ScmConfigValidator.Validate(config);
        else
            Assert.Throws<ArgumentException>(() => ScmConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void InvalidIdsAreRejected(string? id) => Assert.False(ScmConfigValidator.IsValidId(id));

    [Fact]
    public void PublicAndMalformedKeysAreRejected()
    {
        using var rsa = RSA.Create(2048);
        Assert.False(ScmConfigValidator.IsValidPrivateKey(rsa.ExportRSAPublicKeyPem()));
        Assert.False(ScmConfigValidator.IsValidPrivateKey("invalid"));
    }
}
