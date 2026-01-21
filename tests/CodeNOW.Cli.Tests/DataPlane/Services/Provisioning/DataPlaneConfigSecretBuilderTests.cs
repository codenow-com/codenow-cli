using System.Text;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class DataPlaneConfigSecretBuilderTests
{
    [Fact]
    public void Build_IncludesS3KeysWhenAccessKeyAuth()
    {
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" }, Cni = { Name = "system" }, CiPipelines = { Name = "system" } } },
            S3 =
            {
                Enabled = true,
                AuthenticationMethod = S3AuthenticationMethod.AccessKeySecretKey,
                AccessKey = "access",
                SecretKey = "secret",
                Region = "eu-north-1"
            },
            NpmRegistry = { Url = "https://registry.example.com", AccessToken = "token" }
        };

        var builder = new DataPlaneConfigSecretBuilder();
        var secret = builder.Build(config);

        Assert.Contains(DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessKey, secret.Data.Keys);
        Assert.Contains(DataPlaneConstants.DataPlaneConfigKeyS3StorageSecretKey, secret.Data.Keys);
        Assert.Contains(DataPlaneConstants.DataPlaneConfigKeyS3StorageRegion, secret.Data.Keys);

        var npmrc = Encoding.UTF8.GetString(secret.Data[DataPlaneConstants.DataPlaneConfigKeyNpmrc]);
        Assert.Contains("registry=", npmrc);
    }
}
