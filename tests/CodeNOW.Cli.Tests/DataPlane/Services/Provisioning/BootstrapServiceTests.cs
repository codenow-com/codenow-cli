using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class BootstrapServiceTests
{
    [Fact]
    public async Task BootstrapAsync_InvokesProvisioners()
    {
        var config = new OperatorConfig();
        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            operatorProvisioner,
            stackProvisioner);

        await service.BootstrapAsync(config);

        Assert.True(namespaces.Called);
        Assert.True(operatorProvisioner.AppliedCrds);
        Assert.True(operatorProvisioner.AppliedRbac);
        Assert.True(operatorProvisioner.AppliedDeployment);
        Assert.True(operatorProvisioner.WaitedForReady);
        Assert.True(stackProvisioner.AppliedStack);
    }

    [Fact]
    public async Task BootstrapAsync_CreatesClientOnce()
    {
        var config = new OperatorConfig();
        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            operatorProvisioner,
            stackProvisioner);

        await service.BootstrapAsync(config);

        Assert.Equal(1, factory.CreateCalls);
    }

    private sealed class FakeKubernetesClientFactory : IKubernetesClientFactory
    {
        public int CreateCalls { get; private set; }

        public IKubernetesClient Create(Microsoft.Extensions.Logging.ILogger logger, KubernetesConnectionOptions options)
        {
            CreateCalls++;
            return new TestDoubles.Kubernetes.FakeKubernetesClient();
        }
    }

    private sealed class FakeNamespaceProvisioner : INamespaceProvisioner
    {
        public bool Called { get; private set; }

        public NamespaceProvisioningTasks StartNamespaceProvisioning(IKubernetesClient client, OperatorConfig config)
        {
            Called = true;
            return new NamespaceProvisioningTasks(Task.CompletedTask, Task.CompletedTask, Task.CompletedTask);
        }
    }

    private sealed class FakePulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public bool AppliedCrds { get; private set; }
        public bool AppliedRbac { get; private set; }
        public bool AppliedDeployment { get; private set; }
        public bool WaitedForReady { get; private set; }

        public Task ApplyCrdManifestsAsync(IKubernetesClient client)
        {
            AppliedCrds = true;
            return Task.CompletedTask;
        }

        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace)
        {
            AppliedRbac = true;
            return Task.CompletedTask;
        }

        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config)
        {
            AppliedDeployment = true;
            return Task.CompletedTask;
        }

        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
        {
            WaitedForReady = true;
            return Task.CompletedTask;
        }

        public string GetOperatorImage(OperatorConfig config) => "image";
    }

    private sealed class FakePulumiStackProvisioner : IPulumiStackProvisioner
    {
        public bool AppliedStack { get; private set; }

        public Task ApplyPulumiStackRbacAsync(
            IKubernetesClient client,
            string namespaceName,
            string serviceAccountName,
            OperatorConfig config,
            IEnumerable<string> targetNamespaces)
            => Task.CompletedTask;

        public Task ApplyPulumiStackAsync(IKubernetesClient client, string serviceAccountName, OperatorConfig config)
        {
            AppliedStack = true;
            return Task.CompletedTask;
        }

        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;

        public Task CreatePulumiStatePvcAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;
    }
}
