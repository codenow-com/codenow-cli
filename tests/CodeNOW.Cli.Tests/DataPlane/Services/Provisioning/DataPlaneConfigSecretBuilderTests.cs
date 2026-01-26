using System.Text;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class DataPlaneConfigSecretBuilderTests
{
    [Fact]
    public void Build_IncludesProxyFlagsNamespacesAndNpmrc()
    {
        var builder = new DataPlaneConfigSecretBuilder();
        var config = new OperatorConfig
        {
            Kubernetes =
            {
                Namespaces =
                {
                    System = { Name = "system" },
                    Cni = { Name = "system" },
                    CiPipelines = { Name = "ci" }
                }
            },
            HttpProxy =
            {
                Enabled = true,
                Hostname = "proxy.example.com",
                Port = 3128,
                NoProxy = ".cluster.local"
            },
            NpmRegistry =
            {
                Url = "registry.example.com/npm",
                AccessToken = "token"
            }
        };

        var secret = builder.Build(config);
        var data = secret.Data!;

        Assert.Equal("system", GetString(data, DataPlaneConstants.DataPlaneConfigKeyTargetNamespace));
        Assert.Equal("false", GetString(data, DataPlaneConstants.DataPlaneConfigKeyCniDedicatedNamespaceEnabled));
        Assert.Equal("true", GetString(data, DataPlaneConstants.DataPlaneConfigKeyCiPipelinesDedicatedNamespaceEnabled));

        Assert.Equal("true", GetString(data, DataPlaneConstants.DataPlaneConfigKeyHttpProxyEnabled));
        Assert.Equal("proxy.example.com", GetString(data, DataPlaneConstants.DataPlaneConfigKeyHttpProxyHostname));
        Assert.Equal("3128", GetString(data, DataPlaneConstants.DataPlaneConfigKeyHttpProxyPort));
        Assert.Equal(".cluster.local", GetString(data, DataPlaneConstants.DataPlaneConfigKeyHttpProxyNoProxyHostnames));

        Assert.Equal(
            "registry=https://registry.example.com/npm\n//registry.example.com/npm/:_authToken=token\n",
            GetString(data, DataPlaneConstants.DataPlaneConfigKeyNpmrc));
    }

    [Fact]
    public void Build_AddsContainerRegistryConfigJsonWhenCredentialsProvided()
    {
        var builder = new DataPlaneConfigSecretBuilder();
        var config = new OperatorConfig
        {
            ContainerRegistry =
            {
                Hostname = "registry.example.com",
                Username = "user",
                Password = "pass"
            }
        };

        var secret = builder.Build(config);
        var data = secret.Data!;

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        var expected = $"{{\"auths\":{{\"registry.example.com\":{{\"auth\":\"{auth}\"}}}}}}";

        Assert.Equal(expected, GetString(data, DataPlaneConstants.DataPlaneConfigKeyContainerRegistryConfigJson));
    }

    [Fact]
    public void Build_AddsCustomCaAndS3AccessKeyCredentials()
    {
        var builder = new DataPlaneConfigSecretBuilder();
        var config = new OperatorConfig
        {
            Security = { CustomCaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("CA")) },
            S3 =
            {
                Enabled = true,
                AuthenticationMethod = S3AuthenticationMethod.AccessKeySecretKey,
                AccessKey = "access",
                SecretKey = "secret",
                Region = "eu-central-1"
            }
        };

        var secret = builder.Build(config);
        var data = secret.Data!;

        Assert.Equal("CA", GetString(data, DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert));
        Assert.Equal("true", GetString(data, DataPlaneConstants.DataPlaneConfigKeyS3Enabled));
        Assert.Equal("access", GetString(data, DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessKey));
        Assert.Equal("secret", GetString(data, DataPlaneConstants.DataPlaneConfigKeyS3StorageSecretKey));
        Assert.Equal("eu-central-1", GetString(data, DataPlaneConstants.DataPlaneConfigKeyS3StorageRegion));
    }

    private static string GetString(IDictionary<string, byte[]> data, string key)
        => Encoding.UTF8.GetString(data[key]);
}
