using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class PulumiStackManifestBuilderTests
{
    [Fact]
    public void BuildStack_AddsAwsEnvRefsWhenAccessKeyAuth()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm = { Url = "https://git.example.com/repo" },
            S3 =
            {
                Enabled = true,
                AuthenticationMethod = S3AuthenticationMethod.AccessKeySecretKey,
                Url = "s3://bucket",
                AccessKey = "access",
                SecretKey = "secret"
            }
        };

        var stack = builder.BuildStack(config, "sa");
        var envRefs = stack["spec"]!.AsObject()["envRefs"]!.AsObject();

        Assert.NotNull(envRefs["AWS_ACCESS_KEY_ID"]);
        Assert.NotNull(envRefs["AWS_SECRET_ACCESS_KEY"]);
        Assert.Equal("s3://bucket", stack["spec"]!.AsObject()["backend"]!.GetValue<string>());
    }

    private static PulumiStackManifestBuilder BuildBuilder()
    {
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var configSecretBuilder = new DataPlaneConfigSecretBuilder();
        var operatorInfoProvider = new FakeOperatorInfoProvider();
        return new PulumiStackManifestBuilder(operatorProvisioner, configSecretBuilder, operatorInfoProvider);
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client) => Task.CompletedTask;
        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace) => Task.CompletedTask;
        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout) => Task.CompletedTask;
        public string GetOperatorImage(OperatorConfig config) => "operator-image";
    }

    private sealed class FakeOperatorInfoProvider : IOperatorInfoProvider
    {
        public OperatorInfo GetInfo() => new("operator", "1.0.0", "runtime", "plugins");
    }
}
