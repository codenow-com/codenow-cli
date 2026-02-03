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
        var fluxcdProvisioner = new FakeFluxCDProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
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
    public async Task BootstrapAsync_SkipsPulumiCrdsWhenDisabled()
    {
        var config = new OperatorConfig();
        config.Pulumi.InstallCrds = false;

        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var fluxcdProvisioner = new FakeFluxCDProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
            operatorProvisioner,
            stackProvisioner);

        await service.BootstrapAsync(config);

        Assert.False(operatorProvisioner.AppliedCrds);
    }

    [Fact]
    public async Task BootstrapAsync_WaitsForFluxcdCrdsBeforeSourceController()
    {
        var config = new OperatorConfig();
        config.FluxCD = new FluxCDConfig
        {
            Enabled = true,
            InstallCrds = true
        };

        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var fluxcdProvisioner = new SequencedFluxCDProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
            operatorProvisioner,
            stackProvisioner);

        var bootstrapTask = service.BootstrapAsync(config);

        await fluxcdProvisioner.CrdStarted.Task;
        Assert.False(fluxcdProvisioner.SourceControllerCalled);

        fluxcdProvisioner.AllowCrdComplete.SetResult(true);
        await bootstrapTask;

        Assert.True(fluxcdProvisioner.SourceControllerCalled);
        Assert.False(fluxcdProvisioner.SourceControllerCalledBeforeCrdComplete);
    }

    [Fact]
    public async Task BootstrapAsync_WaitsForRbacAndCrdsBeforeDeployment()
    {
        var config = new OperatorConfig();
        config.Pulumi.InstallCrds = true;

        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var fluxcdProvisioner = new FakeFluxCDProvisioner();
        var operatorProvisioner = new SequencedPulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
            operatorProvisioner,
            stackProvisioner);

        var bootstrapTask = service.BootstrapAsync(config);

        Assert.False(operatorProvisioner.DeploymentCalled);

        operatorProvisioner.AllowRbacComplete.SetResult(true);
        operatorProvisioner.AllowCrdsComplete.SetResult(true);

        await bootstrapTask;

        Assert.True(operatorProvisioner.DeploymentCalled);
        Assert.True(operatorProvisioner.DeploymentCalledAfterRbacAndCrds);
    }

    [Fact]
    public async Task BootstrapAsync_CreatesClientOnce()
    {
        var config = new OperatorConfig();
        var factory = new FakeKubernetesClientFactory();
        var namespaces = new FakeNamespaceProvisioner();
        var fluxcdProvisioner = new FakeFluxCDProvisioner();
        var operatorProvisioner = new FakePulumiOperatorProvisioner();
        var stackProvisioner = new FakePulumiStackProvisioner();

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
            operatorProvisioner,
            stackProvisioner);

        await service.BootstrapAsync(config);

        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task BootstrapAsync_CallsProvisionersInExactOrder()
    {
        var calls = new List<string>();
        var config = new OperatorConfig();
        config.Pulumi.InstallCrds = true;
        config.FluxCD = new FluxCDConfig
        {
            Enabled = true,
            InstallCrds = true
        };
        config.Kubernetes.Namespaces.System.Name = "system";
        config.Kubernetes.Namespaces.Cni.Name = "cni";
        config.Kubernetes.Namespaces.CiPipelines.Name = "ci";

        var factory = new FakeKubernetesClientFactory();
        var namespaces = new OrderedNamespaceProvisioner(calls);
        var fluxcdProvisioner = new OrderedFluxCDProvisioner(calls);
        var operatorProvisioner = new OrderedPulumiOperatorProvisioner(calls);
        var stackProvisioner = new OrderedPulumiStackProvisioner(calls);

        var service = new BootstrapService(
            new NullLogger<BootstrapService>(),
            factory,
            new KubernetesConnectionOptions(),
            namespaces,
            fluxcdProvisioner,
            operatorProvisioner,
            stackProvisioner);

        await service.BootstrapAsync(config);

        Assert.Equal(
            new[]
            {
                "namespace.StartNamespaceProvisioning",
                "operator.ApplyCrdManifestsAsync",
                "fluxcd.ApplyCrdManifestsAsync",
                "operator.CreateDataPlaneConfigSecretAsync",
                "fluxcd.ApplySourceControllerAsync",
                "fluxcd.WaitForSourceControllerReadyAsync",
                "operator.ApplyRbacManifestsAsync",
                "operator.ApplyOperatorDeploymentAsync",
                "operator.WaitForOperatorReadyAsync",
                "stack.ApplyPulumiStackRbacAsync",
                "stack.CreatePulumiStatePvcAsync",
                "stack.ApplyPulumiStackAsync"
            },
            calls);
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
        public bool CreatedConfigSecret { get; private set; }

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

        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config)
        {
            CreatedConfigSecret = true;
            return Task.CompletedTask;
        }

        public string GetOperatorImage(OperatorConfig config) => "image";
    }

    private sealed class FakeFluxCDProvisioner : IFluxCDProvisioner
    {
        public Task ApplyCrdManifestsAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;

        public Task ApplySourceControllerAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;

        public Task WaitForSourceControllerReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
            => Task.CompletedTask;
    }

    private sealed class SequencedFluxCDProvisioner : IFluxCDProvisioner
    {
        public TaskCompletionSource<bool> CrdStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AllowCrdComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SourceControllerCalled { get; private set; }
        public bool SourceControllerCalledBeforeCrdComplete { get; private set; }

        public Task ApplyCrdManifestsAsync(IKubernetesClient client, OperatorConfig config)
        {
            CrdStarted.TrySetResult(true);
            return AllowCrdComplete.Task;
        }

        public Task ApplySourceControllerAsync(IKubernetesClient client, OperatorConfig config)
        {
            SourceControllerCalled = true;
            SourceControllerCalledBeforeCrdComplete = !AllowCrdComplete.Task.IsCompleted;
            return Task.CompletedTask;
        }

        public Task WaitForSourceControllerReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
            => Task.CompletedTask;
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

        public Task CreatePulumiStatePvcAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;
    }

    private sealed class SequencedPulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        public TaskCompletionSource<bool> AllowCrdsComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AllowRbacComplete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DeploymentCalled { get; private set; }
        public bool DeploymentCalledAfterRbacAndCrds { get; private set; }

        public Task ApplyCrdManifestsAsync(IKubernetesClient client)
            => AllowCrdsComplete.Task;

        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace)
            => AllowRbacComplete.Task;

        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config)
        {
            DeploymentCalled = true;
            DeploymentCalledAfterRbacAndCrds =
                AllowCrdsComplete.Task.IsCompleted && AllowRbacComplete.Task.IsCompleted;
            return Task.CompletedTask;
        }

        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
            => Task.CompletedTask;

        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config)
            => Task.CompletedTask;

        public string GetOperatorImage(OperatorConfig config) => "image";
    }

    private sealed class OrderedNamespaceProvisioner : INamespaceProvisioner
    {
        private readonly List<string> calls;

        public OrderedNamespaceProvisioner(List<string> calls)
        {
            this.calls = calls;
        }

        public NamespaceProvisioningTasks StartNamespaceProvisioning(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("namespace.StartNamespaceProvisioning");
            return new NamespaceProvisioningTasks(Task.CompletedTask, Task.CompletedTask, Task.CompletedTask);
        }
    }

    private sealed class OrderedFluxCDProvisioner : IFluxCDProvisioner
    {
        private readonly List<string> calls;

        public OrderedFluxCDProvisioner(List<string> calls)
        {
            this.calls = calls;
        }

        public Task ApplyCrdManifestsAsync(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("fluxcd.ApplyCrdManifestsAsync");
            return Task.CompletedTask;
        }

        public Task ApplySourceControllerAsync(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("fluxcd.ApplySourceControllerAsync");
            return Task.CompletedTask;
        }

        public Task WaitForSourceControllerReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
        {
            calls.Add("fluxcd.WaitForSourceControllerReadyAsync");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderedPulumiOperatorProvisioner : IPulumiOperatorProvisioner
    {
        private readonly List<string> calls;

        public OrderedPulumiOperatorProvisioner(List<string> calls)
        {
            this.calls = calls;
        }

        public Task ApplyCrdManifestsAsync(IKubernetesClient client)
        {
            calls.Add("operator.ApplyCrdManifestsAsync");
            return Task.CompletedTask;
        }

        public Task ApplyRbacManifestsAsync(IKubernetesClient client, string targetNamespace)
        {
            calls.Add("operator.ApplyRbacManifestsAsync");
            return Task.CompletedTask;
        }

        public Task ApplyOperatorDeploymentAsync(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("operator.ApplyOperatorDeploymentAsync");
            return Task.CompletedTask;
        }

        public Task WaitForOperatorReadyAsync(IKubernetesClient client, string namespaceName, TimeSpan timeout)
        {
            calls.Add("operator.WaitForOperatorReadyAsync");
            return Task.CompletedTask;
        }

        public Task CreateDataPlaneConfigSecretAsync(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("operator.CreateDataPlaneConfigSecretAsync");
            return Task.CompletedTask;
        }

        public string GetOperatorImage(OperatorConfig config) => "image";
    }

    private sealed class OrderedPulumiStackProvisioner : IPulumiStackProvisioner
    {
        private readonly List<string> calls;

        public OrderedPulumiStackProvisioner(List<string> calls)
        {
            this.calls = calls;
        }

        public Task ApplyPulumiStackRbacAsync(
            IKubernetesClient client,
            string namespaceName,
            string serviceAccountName,
            OperatorConfig config,
            IEnumerable<string> targetNamespaces)
        {
            calls.Add("stack.ApplyPulumiStackRbacAsync");
            return Task.CompletedTask;
        }

        public Task ApplyPulumiStackAsync(IKubernetesClient client, string serviceAccountName, OperatorConfig config)
        {
            calls.Add("stack.ApplyPulumiStackAsync");
            return Task.CompletedTask;
        }

        public Task CreatePulumiStatePvcAsync(IKubernetesClient client, OperatorConfig config)
        {
            calls.Add("stack.CreatePulumiStatePvcAsync");
            return Task.CompletedTask;
        }
    }
}
