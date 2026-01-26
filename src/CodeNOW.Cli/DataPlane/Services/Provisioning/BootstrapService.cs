using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Defines bootstrap operations for data plane components and supporting infrastructure.
/// </summary>
public interface IBootstrapService
{
    /// <summary>
    /// Bootstraps or updates the data plane operator and its dependencies.
    /// </summary>
    /// <param name="config">
    /// Operator configuration, including namespace layout, SCM access, optional FluxCD integration,
    /// image versions, and storage settings.
    /// </param>
    /// <remarks>
    /// This operation provisions namespaces, RBAC, CRDs (when enabled), operator deployments,
    /// secrets, and Pulumi stack resources. It is safe to call multiple times to converge state.
    /// </remarks>
    Task BootstrapAsync(OperatorConfig config);
}

/// <summary>
/// Installs and configures the data plane operator and its dependencies.
/// </summary>
public class BootstrapService : IBootstrapService
{
    private readonly ILogger<BootstrapService> logger;
    private readonly IKubernetesClientFactory kubernetesClientFactory;
    private readonly KubernetesConnectionOptions connectionOptions;
    private readonly INamespaceProvisioner namespaceProvisioner;
    private readonly IFluxCDProvisioner fluxcdProvisioner;
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
        IFluxCDProvisioner fluxcdProvisioner,
        IPulumiOperatorProvisioner operatorProvisioner,
        IPulumiStackProvisioner stackProvisioner)
    {
        this.logger = logger;
        this.kubernetesClientFactory = kubernetesClientFactory;
        this.connectionOptions = connectionOptions;
        this.namespaceProvisioner = namespaceProvisioner;
        this.fluxcdProvisioner = fluxcdProvisioner;
        this.operatorProvisioner = operatorProvisioner;
        this.stackProvisioner = stackProvisioner;
    }

    /// <summary>
    /// Provisions namespaces, RBAC, CRDs (when enabled), FluxCD components, secrets,
    /// and the Pulumi stack resources needed for the data plane.
    /// </summary>
    /// <param name="config">Operator configuration used to drive provisioning decisions.</param>
    /// <remarks>
    /// This method is idempotent and can be called repeatedly to converge cluster state.
    /// It waits for readiness where required before proceeding to dependent steps.
    /// </remarks>
    public async Task BootstrapAsync(OperatorConfig config)
    {
        logger.LogInformation("Starting bootstrap...");

        var client = kubernetesClientFactory.Create(logger, connectionOptions);

        var namespaceTasks = namespaceProvisioner.StartNamespaceProvisioning(client, config);
        Task? crdTask = null;
        if (config.Pulumi.InstallCrds)
            crdTask = operatorProvisioner.ApplyCrdManifestsAsync(client);

        Task? fluxcdCrdTask = null;
        if (config.FluxCD?.Enabled == true && config.FluxCD.InstallCrds)
            fluxcdCrdTask = fluxcdProvisioner.ApplyCrdManifestsAsync(client, config);

        await namespaceTasks.SystemNamespace;
        if (config.FluxCD?.Enabled == true)
        {
            await (fluxcdCrdTask ?? Task.CompletedTask);
            await fluxcdProvisioner.ApplySourceControllerAsync(
                client,
                config);
            await fluxcdProvisioner.WaitForSourceControllerReadyAsync(
                client,
                config.Kubernetes.Namespaces.System.Name,
                TimeSpan.FromMinutes(5));
        }
        var rbacTask = operatorProvisioner.ApplyRbacManifestsAsync(client, config.Kubernetes.Namespaces.System.Name);
        await Task.WhenAll(
            rbacTask,
            crdTask ?? Task.CompletedTask);
        var deploymentTask = operatorProvisioner.ApplyOperatorDeploymentAsync(client, config);

        await Task.WhenAll(
            namespaceTasks.CniNamespace,
            namespaceTasks.CiPipelinesNamespace,
            deploymentTask);

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
