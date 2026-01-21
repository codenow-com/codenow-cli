using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Installs and configures the data plane operator and its dependencies.
/// </summary>
public class BootstrapService : IBootstrapService
{
    private readonly ILogger<BootstrapService> logger;
    private readonly IKubernetesClientFactory kubernetesClientFactory;
    private readonly KubernetesConnectionOptions connectionOptions;
    private readonly INamespaceProvisioner namespaceProvisioner;
    private readonly IPulumiOperatorProvisioner operatorProvisioner;
    private readonly IPulumiStackProvisioner stackProvisioner;

    /// <summary>
    /// Creates a bootstrap service.
    /// </summary>
    public BootstrapService(
        ILogger<BootstrapService> logger,
        IKubernetesClientFactory kubernetesClientFactory,
        KubernetesConnectionOptions connectionOptions,
        INamespaceProvisioner namespaceProvisioner,
        IPulumiOperatorProvisioner operatorProvisioner,
        IPulumiStackProvisioner stackProvisioner)
    {
        this.logger = logger;
        this.kubernetesClientFactory = kubernetesClientFactory;
        this.connectionOptions = connectionOptions;
        this.namespaceProvisioner = namespaceProvisioner;
        this.operatorProvisioner = operatorProvisioner;
        this.stackProvisioner = stackProvisioner;
    }

    /// <inheritdoc />
    public async Task BootstrapAsync(OperatorConfig config)
    {
        logger.LogInformation("Starting bootstrap...");

        var client = kubernetesClientFactory.Create(logger, connectionOptions);
        var namespaceTasks = namespaceProvisioner.StartNamespaceProvisioning(client, config);
        var crdTask = operatorProvisioner.ApplyCrdManifestsAsync(client);

        await namespaceTasks.SystemNamespace;
        var rbacTask = operatorProvisioner.ApplyRbacManifestsAsync(client, config.Kubernetes.Namespaces.System.Name);
        var deploymentTask = operatorProvisioner.ApplyOperatorDeploymentAsync(client, config);

        await Task.WhenAll(
            namespaceTasks.CniNamespace,
            namespaceTasks.CiPipelinesNamespace,
            rbacTask,
            deploymentTask,
            crdTask);

        await operatorProvisioner.WaitForOperatorReadyAsync(
            client,
            config.Kubernetes.Namespaces.System.Name,
            TimeSpan.FromMinutes(5));

        var serviceAccountName = DataPlaneConstants.ServiceAccountName;
        var stackRbacTask = stackProvisioner.ApplyPulumiStackRbacAsync(
            client,
            config.Kubernetes.Namespaces.System.Name,
            serviceAccountName,
            config,
            [
                config.Kubernetes.Namespaces.System.Name,
                config.Kubernetes.Namespaces.Cni.Name,
                config.Kubernetes.Namespaces.CiPipelines.Name
            ]);
        var operatorConfigSecretTask = stackProvisioner.CreateDataPlaneConfigSecretAsync(client, config);
        var statePvcTask = stackProvisioner.CreatePulumiStatePvcAsync(client, config);

        await Task.WhenAll(stackRbacTask, operatorConfigSecretTask, statePvcTask);
        await stackProvisioner.ApplyPulumiStackAsync(client, serviceAccountName, config);

        logger.LogInformation("Bootstrap finished.");
    }
}
