using System.Text.Json;
using CodeNOW.Cli.Common.Security;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Serialization;
using CodeNOW.Cli.DataPlane.Services.Operations;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class OperatorConfigServiceTests
{
    [Fact]
    public void EncryptConfigFile_EncryptsSecretValuesAndOverwritesFile()
    {
        var tempDir = Directory.CreateTempSubdirectory(nameof(OperatorConfigServiceTests));
        try
        {
            var filePath = Path.Combine(tempDir.FullName, "operator.json");
            var inputConfig = BuildConfig();
            var plaintextJson = JsonSerializer.Serialize(
                inputConfig,
                OperatorConfigJsonContext.Default.OperatorConfig);
            File.WriteAllText(filePath, plaintextJson);

            var service = new OperatorConfigService();
            var encryptionKey = service.EncryptConfigFile(filePath);

            Assert.False(string.IsNullOrWhiteSpace(encryptionKey));
            Assert.NotEqual(plaintextJson, File.ReadAllText(filePath));

            var encryptedJson = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(encryptedJson);
            var root = doc.RootElement;

            var username = root.GetProperty("ContainerRegistry").GetProperty("Username").GetString();
            var accessToken = root.GetProperty("NpmRegistry").GetProperty("AccessToken").GetString();
            var passphrase = root.GetProperty("Pulumi").GetProperty("Passphrase").GetString();

            Assert.StartsWith(SecretProtector.Prefix, username, StringComparison.Ordinal);
            Assert.StartsWith(SecretProtector.Prefix, accessToken, StringComparison.Ordinal);
            Assert.StartsWith(SecretProtector.Prefix, passphrase, StringComparison.Ordinal);

            var typeInfo = OperatorConfigJsonTypeInfoFactory.Create(() => encryptionKey);
            var decrypted = JsonSerializer.Deserialize(encryptedJson, typeInfo);

            Assert.NotNull(decrypted);
            Assert.Equal(inputConfig.ContainerRegistry.Hostname, decrypted!.ContainerRegistry.Hostname);
            Assert.Equal(inputConfig.ContainerRegistry.Username, decrypted.ContainerRegistry.Username);
            Assert.Equal(inputConfig.ContainerRegistry.Password, decrypted.ContainerRegistry.Password);
            Assert.Equal(inputConfig.NpmRegistry.AccessToken, decrypted.NpmRegistry.AccessToken);
            Assert.Equal(inputConfig.Pulumi.Passphrase, decrypted.Pulumi.Passphrase);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void EncryptConfigFile_ThrowsWhenPathIsEmpty()
    {
        var service = new OperatorConfigService();

        Assert.Throws<ArgumentException>(() => service.EncryptConfigFile(""));
    }

    [Fact]
    public void EncryptConfigFile_ThrowsWhenFileMissing()
    {
        var service = new OperatorConfigService();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        Assert.Throws<FileNotFoundException>(() => service.EncryptConfigFile(path));
    }

    private static OperatorConfig BuildConfig()
    {
        return new OperatorConfig
        {
            ContainerRegistry =
            {
                Hostname = "registry.example.com",
                Username = "user",
                Password = "pass"
            },
            NpmRegistry =
            {
                Url = "https://npm.example.com",
                AccessToken = "npm-token"
            },
            Scm =
            {
                Url = "https://git.example.com",
                AuthenticationMethod = ScmAuthenticationMethod.UsernamePassword,
                Username = "scm-user",
                Password = "scm-pass"
            },
            Kubernetes =
            {
                Namespaces =
                {
                    System = { Name = "cn-system" },
                    Cni = { Name = "cn-cni" },
                    CiPipelines = { Name = "cn-ci" }
                },
                NodeLabels =
                {
                    System = { Key = "sys", Value = "sys-val" },
                    Application = { Key = "app", Value = "app-val" }
                }
            },
            S3 =
            {
                Enabled = true,
                AuthenticationMethod = S3AuthenticationMethod.AccessKeySecretKey,
                AccessKey = "s3-access",
                SecretKey = "s3-secret",
                Region = "us-east-1"
            },
            Environment =
            {
                Name = "dev"
            },
            Pulumi =
            {
                Passphrase = "pulumi-pass",
                Images =
                {
                    RuntimeVersion = "3.0.0",
                    PluginsVersion = "1.0.0"
                }
            }
        };
    }
}
