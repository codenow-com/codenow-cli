using System.Text;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Console.Commands;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class InstallServiceTests
{
    private const string Instance = "5e613c15-c12b-4f65-94ef-83be1709d76c";

    private const string Minimal = """
    {
      "controlPlaneUrl": "https://cloud.codenow.com",
      "authorizationToken": "tok",
      "containerRegistrySystemHostname": "registry.example",
      "containerRegistrySystem": { "username": "u", "password": "p" }
    }
    """;

    private sealed class Factory(IKubernetesClient client) : IKubernetesClientFactory
    {
        public IKubernetesClient Create(ILogger logger, KubernetesConnectionOptions options) => client;
    }

    private readonly FakeKubernetesClient client = new();

    private InstallService Service() =>
        new(NullLogger<InstallService>.Instance, new Factory(client), new KubernetesConnectionOptions());

    private static InstallConfig Config(string json = Minimal) => InstallConfigLoader.Parse(json);

    private T Applied<T>() where T : class => client.AppliedObjects.OfType<T>().Single();

    [Fact]
    public void NamespaceDefaultsAndHostnameIsRequired()
    {
        Assert.Equal("cn-data-plane-system", Config().NamespaceName);

        var missing = Minimal.Replace("\"containerRegistrySystemHostname\": \"registry.example\",", string.Empty);
        Assert.Throws<InstallConfigException>(() => Config(missing));
    }

    [Theory]
    [InlineData("""{"controlPlaneUrl":"http://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s"}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"","containerRegistrySystemHostname":"r","imagePullSecret":"s"}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r"}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s","containerRegistrySystem":{"username":"u","password":"p"}}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","containerRegistrySystem":{"username":"u"}}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"https://r","imagePullSecret":"s"}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s","unknown":1}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s","namespaceName":"Bad_Name"}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s","httpProxy":{"hostname":"http://p","port":1}}""")]
    [InlineData("""{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"s","httpProxy":{"hostname":"p","port":70000}}""")]
    public void InvalidInputIsRejected(string json)
    {
        Assert.Throws<InstallConfigException>(() => Config(json));
    }

    [Fact]
    public void ErrorsDoNotQuoteTheToken()
    {
        var ex = Assert.Throws<InstallConfigException>(() => Config("""{"authorizationToken":"super-secret","unknown":1}"""));

        Assert.DoesNotContain("super-secret", ex.Message);
    }

    [Fact]
    public async Task NewInstallationEnablesCronJobLastWithLatest()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        Assert.IsType<V1CronJob>(client.AppliedObjects.Last());
        var cronJob = Applied<V1CronJob>();
        var pod = cronJob.Spec.JobTemplate.Spec.Template.Spec;
        Assert.Equal("registry.example/codenow/data-plane-update-controller:latest", pod.Containers.Single().Image);
        Assert.Equal("Always", pod.Containers.Single().ImagePullPolicy);
        Assert.Equal("*/5 * * * *", cronJob.Spec.Schedule);
        Assert.Equal("Forbid", cronJob.Spec.ConcurrencyPolicy);
        Assert.Equal((1, 3), (cronJob.Spec.SuccessfulJobsHistoryLimit, cronJob.Spec.FailedJobsHistoryLimit));
        Assert.Equal(0, cronJob.Spec.JobTemplate.Spec.BackoffLimit);
        Assert.Null(cronJob.Spec.JobTemplate.Spec.TtlSecondsAfterFinished);
        Assert.Equal("Never", pod.RestartPolicy);
        Assert.Null(pod.Containers.Single().Env);
        Assert.Equal("cn-cli", cronJob.Spec.JobTemplate.Spec.Template.Metadata.Labels["app.kubernetes.io/managed-by"]);
        Assert.Equal("cn-system-image-pull-secret", pod.ImagePullSecrets.Single().Name);
    }

    [Fact]
    public async Task ClusterRoleHoldsTheDelegatedProfileWithoutListedEscalateOrBind()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var role = Applied<V1ClusterRole>();
        Assert.Contains(role.Rules, r => r.ApiGroups?.Contains("*") == true && r.Verbs.Contains("*"));
        Assert.DoesNotContain(role.Rules, r => r.Verbs.Contains("escalate") || r.Verbs.Contains("bind"));
    }

    [Fact]
    public async Task NamedRulesKeepCreateSeparate()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var rules = Applied<V1Role>().Rules;
        Assert.DoesNotContain(rules, r => r.Verbs.Contains("create") && r.ResourceNames is not null);
        Assert.DoesNotContain(rules, r => r.Resources.Contains("pods/log") || r.Verbs.Contains("watch"));
    }

    [Fact]
    public async Task RerunPreservesIdentityImageAndOverrides()
    {
        const string pinned = "registry.example/codenow/data-plane-update-controller@sha256:abc";
        client.Install.ConfigMaps["cn-dp-identity"] = new V1ConfigMap { Data = new Dictionary<string, string> { ["clusterInstanceId"] = Instance } };
        client.Install.ConfigMaps["cn-dp-update-controller-config"] = new V1ConfigMap
        {
            Data = new Dictionary<string, string> { ["config.json"] = """{"deploymentMaxAttempts":9}""" },
        };
        client.Install.CronJob = new InstallManifestBuilder(Config(), Instance).CronJob(pinned);

        await Service().InstallAsync(Config(), recoverController: false);

        var cronJob = Applied<V1CronJob>();
        Assert.Equal(pinned, cronJob.Spec.JobTemplate.Spec.Template.Spec.Containers.Single().Image);
        Assert.Equal(Instance, cronJob.Metadata.Labels["app.kubernetes.io/instance"]);
        Assert.Contains("\"deploymentMaxAttempts\":9", client.AppliedObjects.OfType<V1ConfigMap>().Single().Data["config.json"]);
    }

    [Fact]
    public async Task RecoveryRestoresLatest()
    {
        client.Install.CronJob = new InstallManifestBuilder(Config(), Instance).CronJob("registry.example/x@sha256:abc");

        await Service().InstallAsync(Config(), recoverController: true);

        Assert.EndsWith(":latest", Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec.Containers.Single().Image);
    }

    [Fact]
    public async Task OperatorInstallationStopsBeforeMutation()
    {
        client.Install.Secrets.Add(DataPlaneConstants.OperatorConfigSecretName);

        await Assert.ThrowsAsync<InstallBlockedException>(() => Service().InstallAsync(Config(), recoverController: false));

        Assert.Empty(client.AppliedObjects);
        Assert.Empty(client.Install.Namespaces);
    }

    [Fact]
    public async Task InvalidStoredIdentityIsNotReplaced()
    {
        client.Install.ConfigMaps["cn-dp-identity"] = new V1ConfigMap { Data = new Dictionary<string, string> { ["clusterInstanceId"] = "bad" } };

        await Assert.ThrowsAsync<InstallBlockedException>(() => Service().InstallAsync(Config(), recoverController: false));

        Assert.Empty(client.AppliedObjects);
    }

    [Fact]
    public async Task BindingInAnotherNamespaceIsACollision()
    {
        client.Install.ClusterRoleBinding = new V1ClusterRoleBinding
        {
            Subjects = [new Rbacv1Subject { Kind = "ServiceAccount", Name = "x", NamespaceProperty = "other" }],
        };

        await Assert.ThrowsAsync<InstallBlockedException>(() => Service().InstallAsync(Config(), recoverController: false));
    }

    [Fact]
    public async Task ExistingPullSecretIsReusedUnchanged()
    {
        const string json = """{"controlPlaneUrl":"https://x","authorizationToken":"t","containerRegistrySystemHostname":"r","imagePullSecret":"mine"}""";
        client.Install.Secrets.Add("mine");

        await Service().InstallAsync(Config(json), recoverController: false);

        Assert.DoesNotContain(client.AppliedObjects.OfType<V1Secret>(), s => s.Metadata.Name == "mine");
        Assert.Equal("mine", Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec.ImagePullSecrets.Single().Name);
    }

    [Fact]
    public async Task ProxyUsesBracketedIpv6()
    {
        var json = Minimal.Replace("\"authorizationToken\": \"tok\",", "\"authorizationToken\": \"tok\", \"httpProxy\": {\"hostname\":\"2001:db8::1\",\"port\":3128},");

        await Service().InstallAsync(Config(json), recoverController: false);

        var env = Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec.Containers.Single().Env;
        Assert.Equal("http://[2001:db8::1]:3128", env.Single(e => e.Name == "HTTPS_PROXY").Value);
        Assert.Equal(string.Empty, env.Single(e => e.Name == "NO_PROXY").Value);
    }

    [Fact]
    public async Task PullSecretUsesDockerConfigForTheSelectedRegistry()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var secret = client.AppliedObjects.OfType<V1Secret>().Single(s => s.Metadata.Name == "cn-system-image-pull-secret");
        Assert.Equal("kubernetes.io/dockerconfigjson", secret.Type);
        Assert.Contains("registry.example", Encoding.UTF8.GetString(secret.Data[".dockerconfigjson"]));
    }

    [Fact]
    public async Task RerunPersistsTheSuppliedHostnameWithoutRewritingTheImage()
    {
        const string pinned = "old.example/codenow/data-plane-update-controller@sha256:abc";
        client.Install.ConfigMaps["cn-dp-identity"] = new V1ConfigMap { Data = new Dictionary<string, string> { ["clusterInstanceId"] = Instance } };
        client.Install.CronJob = new InstallManifestBuilder(Config(), Instance).CronJob(pinned);

        await Service().InstallAsync(Config(), recoverController: false);

        Assert.Contains("\"containerRegistrySystemHostname\":\"registry.example\"", client.AppliedObjects.OfType<V1ConfigMap>().Single().Data["config.json"]);
        Assert.Equal(pinned, Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec.Containers.Single().Image);
    }

    [Fact]
    public async Task AuthorizationAndRegistryCredentialsStaySeparate()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var secrets = client.AppliedObjects.OfType<V1Secret>().ToList();
        var authorization = secrets.Single(s => s.Metadata.Name == "cn-dp-authorization");
        var pull = secrets.Single(s => s.Metadata.Name == "cn-system-image-pull-secret");

        Assert.Equal("token", Assert.Single(authorization.Data).Key);
        Assert.Equal(".dockerconfigjson", Assert.Single(pull.Data).Key);
        Assert.DoesNotContain("tok", Encoding.UTF8.GetString(pull.Data[".dockerconfigjson"]));
    }

    [Fact]
    public async Task ControllerPodMeetsRestrictedAdmissionWithDocumentedResources()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var pod = Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec;
        var container = pod.Containers.Single();

        Assert.True(pod.SecurityContext.RunAsNonRoot);
        Assert.Equal("RuntimeDefault", pod.SecurityContext.SeccompProfile.Type);
        Assert.False(container.SecurityContext.AllowPrivilegeEscalation);
        Assert.Equal("ALL", container.SecurityContext.Capabilities.Drop.Single());
        Assert.Equal(("100m", "128Mi"), (container.Resources.Requests["cpu"].ToString(), container.Resources.Requests["memory"].ToString()));
        Assert.Equal(("500m", "256Mi"), (container.Resources.Limits["cpu"].ToString(), container.Resources.Limits["memory"].ToString()));
    }

    [Fact]
    public async Task InstallCreatesNoJobAndNoDeployerResources()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        Assert.DoesNotContain(client.AppliedObjects, resource => resource is V1Job);
        Assert.DoesNotContain(client.AppliedObjects, resource => resource.Metadata.Name.Contains("deployer") || resource.Metadata.Name.Contains("pulumi"));
        Assert.DoesNotContain(client.AppliedObjects, resource => resource.Metadata.Name == "cn-dp-status");
    }

    [Fact]
    public async Task ExecutionTemplatesCarryTheOwnershipLabels()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        var cronJob = Applied<V1CronJob>();
        foreach (var labels in new[] { cronJob.Metadata.Labels, cronJob.Spec.JobTemplate.Metadata.Labels, cronJob.Spec.JobTemplate.Spec.Template.Metadata.Labels })
        {
            Assert.Equal("cn-dp-update-controller", labels["app.kubernetes.io/name"]);
            Assert.Equal("update-controller", labels["app.kubernetes.io/component"]);
            Assert.Equal("cn-cli", labels["app.kubernetes.io/managed-by"]);
            Assert.True(Guid.TryParse(labels["app.kubernetes.io/instance"], out _));
        }
    }

    [Fact]
    public async Task ExistingNamespaceIsReused()
    {
        client.Install.Namespaces.Add("cn-data-plane-system");

        await Service().InstallAsync(Config(), recoverController: false);

        Assert.DoesNotContain(client.AppliedObjects, resource => resource is V1Namespace);
        Assert.Equal("cn-data-plane-system", Assert.Single(client.Install.Namespaces));
    }

    [Fact]
    public async Task ConcurrentInstallationReusesTheWinningIdentity()
    {
        client.Install.ConcurrentlyCreatedConfigMap = new V1ConfigMap { Data = new Dictionary<string, string> { ["clusterInstanceId"] = Instance } };

        await Service().InstallAsync(Config(), recoverController: false);

        Assert.Equal(Instance, Applied<V1CronJob>().Metadata.Labels["app.kubernetes.io/instance"]);
    }

    [Fact]
    public async Task RestoredIdentityIsKeptAndAMissingOneIsANewRegistration()
    {
        client.Install.ConfigMaps["cn-dp-identity"] = new V1ConfigMap { Data = new Dictionary<string, string> { ["clusterInstanceId"] = Instance } };
        await Service().InstallAsync(Config(), recoverController: false);
        Assert.Equal(Instance, Applied<V1CronJob>().Metadata.Labels["app.kubernetes.io/instance"]);

        var clone = new FakeKubernetesClient();
        await new InstallService(NullLogger<InstallService>.Instance, new Factory(clone), new KubernetesConnectionOptions()).InstallAsync(Config(), recoverController: false);

        var generated = clone.AppliedObjects.OfType<V1CronJob>().Single().Metadata.Labels["app.kubernetes.io/instance"];
        Assert.NotEqual(Instance, generated);
        Assert.True(Guid.TryParse(generated, out _));
    }

    [Fact]
    public async Task IdentityNeedsNoKubeSystemAccess()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        Assert.DoesNotContain("kube-system", client.Install.ConfigMapReadNamespaces);
    }

    [Fact]
    public async Task OperatorPodsStopBeforeMutation()
    {
        client.CoreV1.ListNamespacedPodAsyncHandler = (_, _, _) => Task.FromResult(new V1PodList { Items = [new V1Pod()] });

        var blocked = await Assert.ThrowsAsync<InstallBlockedException>(() => Service().InstallAsync(Config(), recoverController: false));

        Assert.Contains("transfer state", blocked.Message);
        Assert.Empty(client.AppliedObjects);
    }

    [Fact]
    public async Task ProxyIsPersistedWithTheBypassList()
    {
        var json = Minimal.Replace("\"authorizationToken\": \"tok\",", "\"authorizationToken\": \"tok\", \"httpProxy\": {\"hostname\":\"proxy.test\",\"port\":3128,\"noProxyHostnames\":\"a.test,b.test\"},");

        await Service().InstallAsync(Config(json), recoverController: false);

        var config = client.AppliedObjects.OfType<V1ConfigMap>().Single().Data["config.json"];
        Assert.Contains("\"hostname\":\"proxy.test\"", config);
        Assert.Contains("\"noProxyHostnames\":\"a.test,b.test\"", config);
        var env = Applied<V1CronJob>().Spec.JobTemplate.Spec.Template.Spec.Containers.Single().Env;
        Assert.Equal("http://proxy.test:3128", env.Single(e => e.Name == "HTTP_PROXY").Value);
        Assert.Equal("a.test,b.test", env.Single(e => e.Name == "NO_PROXY").Value);
    }

    [Fact]
    public async Task OmittedProxyAddsNoOverrides()
    {
        await Service().InstallAsync(Config(), recoverController: false);

        Assert.DoesNotContain("httpProxy", client.AppliedObjects.OfType<V1ConfigMap>().Single().Data["config.json"]);
    }

    [Fact]
    public async Task DashboardDetectsTheModelOnlyInTheTargetNamespace()
    {
        Assert.False(await DashboardCommand.UsesUpdateControllerAsync(client.Install, "operator-ns"));
        Assert.Equal("operator-ns", Assert.Single(client.Install.ConfigMapReadNamespaces));

        client.Install.ConfigMaps["cn-dp-identity"] = new V1ConfigMap();
        Assert.True(await DashboardCommand.UsesUpdateControllerAsync(client.Install, "controller-ns"));
    }

    [Fact]
    public void SuccessOutputCoversCronJobJobsPodsAndLogs()
    {
        var commands = InstallCommand.SuccessLines("ns").Where(line => line.Contains("kubectl")).ToList();

        Assert.Contains(commands, line => line.Contains("get cronjob"));
        Assert.Contains(commands, line => line.Contains("get jobs"));
        Assert.Contains(commands, line => line.Contains("get pods"));
        Assert.Equal(2, commands.Count(line => line.Contains(" logs ")));
    }

    [Fact]
    public void SuccessOutputUsesTheActualNamespace()
    {
        var lines = InstallCommand.SuccessLines("custom-ns").ToList();

        Assert.Contains(lines, line => line.Contains("asynchronously"));
        Assert.Contains(lines, line => line.Contains("dashboard"));
        Assert.All(lines.Where(line => line.Contains("kubectl")), line => Assert.Contains("-n custom-ns", line));
    }
}
