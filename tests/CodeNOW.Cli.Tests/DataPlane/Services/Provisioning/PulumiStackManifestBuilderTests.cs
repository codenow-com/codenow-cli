using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane;
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

    [Fact]
    public void BuildStack_FluxcdEnabled_UsesFluxSourceAndOmitsScmFields()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Environment = { Name = "dev" },
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Scm =
            {
                Url = "https://git.example.com/repo",
                AuthenticationMethod = ScmAuthenticationMethod.AccessToken,
                AccessToken = "token"
            },
            FluxCD = new FluxCDConfig
            {
                Enabled = true,
                InstallCrds = true
            }
        };

        var stack = builder.BuildStack(config, "sa");
        var spec = stack["spec"]!.AsObject();

        Assert.Null(spec["projectRepo"]);
        Assert.Null(spec["repoDir"]);
        Assert.Null(spec["resyncFrequencySeconds"]);
        Assert.Null(spec["branch"]);
        Assert.Null(spec["gitAuth"]);

        var fluxSource = spec["fluxSource"]!.AsObject();
        var sourceRef = fluxSource["sourceRef"]!.AsObject();
        Assert.Equal("source.toolkit.fluxcd.io/v1", sourceRef["apiVersion"]!.GetValue<string>());
        Assert.Equal("GitRepository", sourceRef["kind"]!.GetValue<string>());
        Assert.Equal(FluxCDProvisioner.FluxcdGitRepositoryName, sourceRef["name"]!.GetValue<string>());
        Assert.Equal("dev", fluxSource["dir"]!.GetValue<string>());
    }

    [Fact]
    public void BuildStack_UsesFileBackendWhenS3Disabled()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        var stack = builder.BuildStack(config, "sa");

        Assert.Equal($"file:///{PulumiStackManifestBuilder.PulumiStatePath}", stack["spec"]!.AsObject()["backend"]!.GetValue<string>());
    }

    [Fact]
    public void BuildStack_IncludesPulumiEnvRefs()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        var stack = builder.BuildStack(config, "sa");
        var envRefs = stack["spec"]!.AsObject()["envRefs"]!.AsObject();

        Assert.NotNull(envRefs["HOME"]);
        Assert.NotNull(envRefs["PULUMI_CONFIG_PASSPHRASE"]);
    }

    [Fact]
    public void BuildPulumiStatePvc_AppliesBootstrapLabelsAndNamespace()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        var pvc = builder.BuildPulumiStatePvc(config);

        Assert.Equal("system", pvc.Metadata?.NamespaceProperty);
        Assert.NotNull(pvc.Metadata?.Labels);
        foreach (var label in ProvisioningCommonTools.BootstrapLabels)
            Assert.Equal(label.Value, pvc.Metadata!.Labels![label.Key]);
    }

    [Fact]
    public void BuildStack_ConfiguresFetchContainerVolumesAndProxy()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Security = { CustomCaBase64 = Convert.ToBase64String("CA"u8.ToArray()) },
            HttpProxy =
            {
                Enabled = true,
                Hostname = "proxy.example.com",
                Port = 3128,
                NoProxy = ".cluster.local"
            }
        };

        var stack = builder.BuildStack(config, "sa");
        var initContainers = stack["spec"]!
            .AsObject()["workspaceTemplate"]!.AsObject()["spec"]!.AsObject()["podTemplate"]!.AsObject()
            ["spec"]!.AsObject()["initContainers"]!.AsArray();
        var fetch = initContainers.First(node => node?["name"]?.GetValue<string>() == "fetch")!.AsObject();
        var volumeMounts = fetch["volumeMounts"]!.AsArray();

        Assert.Contains(volumeMounts, mount => mount?["name"]?.GetValue<string>() == "tmp");
        Assert.Contains(volumeMounts, mount => mount?["name"]?.GetValue<string>() == "ca-certificates");

        var env = fetch["env"]!.AsArray();
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "HTTP_PROXY");
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "HTTPS_PROXY");
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "NO_PROXY");
    }

    [Fact]
    public void BuildStack_ConfiguresPulumiContainerCustomCaEnv()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } },
            Security = { CustomCaBase64 = Convert.ToBase64String("CA"u8.ToArray()) }
        };

        var stack = builder.BuildStack(config, "sa");
        var podSpec = stack["spec"]!
            .AsObject()["workspaceTemplate"]!.AsObject()["spec"]!.AsObject()["podTemplate"]!.AsObject()
            ["spec"]!.AsObject();
        var containers = podSpec["containers"]!.AsArray();
        var pulumi = containers.First(node => node?["name"]?.GetValue<string>() == "pulumi")!.AsObject();
        var env = pulumi["env"]!.AsArray();

        Assert.Contains(env, node =>
            node?["name"]?.GetValue<string>() == "NODE_EXTRA_CA_CERTS" &&
            node?["value"]?.GetValue<string>() ==
            $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}");
    }

    [Fact]
    public void BuildStack_ConfiguresInstallPluginsContainer()
    {
        var builder = BuildBuilder();
        var config = new OperatorConfig
        {
            Kubernetes = { Namespaces = { System = { Name = "system" } } }
        };

        var stack = builder.BuildStack(config, "sa");
        var initContainers = stack["spec"]!
            .AsObject()["workspaceTemplate"]!.AsObject()["spec"]!.AsObject()["podTemplate"]!.AsObject()
            ["spec"]!.AsObject()["initContainers"]!.AsArray();
        var installPlugins = initContainers.First(node => node?["name"]?.GetValue<string>() == "install-plugins")!.AsObject();

        Assert.NotNull(installPlugins["command"]);
        var mounts = installPlugins["volumeMounts"]!.AsArray();
        Assert.Contains(mounts, mount => mount?["mountPath"]?.GetValue<string>() == PulumiStackManifestBuilder.PulumiHomePath);
    }

    private static PulumiStackManifestBuilder BuildBuilder()
    {
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var operatorInfoProvider = new FakeOperatorInfoProvider();
        return new PulumiStackManifestBuilder(operatorProvisioner, operatorInfoProvider);
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client) => Task.CompletedTask;
        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace) => Task.CompletedTask;
        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout) => Task.CompletedTask;
        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config) => Task.CompletedTask;
        public string GetOperatorImage(OperatorConfig config) => "operator-image";
    }

    private sealed class FakeOperatorInfoProvider : IPulumiOperatorInfoProvider
    {
        public PulumiOperatorInfo GetInfo() => new("operator", "1.0.0", "runtime", "3.2.1", "plugins", "9.9.9");
    }
}
