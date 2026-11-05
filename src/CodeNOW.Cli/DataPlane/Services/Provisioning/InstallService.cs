using System.Text.RegularExpressions;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Models;
using Microsoft.Extensions.Logging;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>Raised when the cluster state does not allow installation; messages are actionable.</summary>
public sealed class InstallBlockedException(string message) : Exception(message);

/// <summary>Installs or reruns the Update Controller. Never contacts the Control Plane or a registry.</summary>
public sealed partial class InstallService(
    ILogger<InstallService> logger,
    IKubernetesClientFactory clientFactory,
    KubernetesConnectionOptions connectionOptions)
{
    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex Uuid();

    /// <summary>Provisions prerequisites first and enables the CronJob last.</summary>
    /// <param name="config">Validated installation document.</param>
    /// <param name="recoverController">Restores the existing CronJob to <c>:latest</c>.</param>
    public async Task InstallAsync(InstallConfig config, bool recoverController)
    {
        var client = clientFactory.Create(logger, connectionOptions);
        var install = client.Install;
        var namespaceName = config.NamespaceName!;

        await RejectOperatorInstallationAsync(client, namespaceName);

        if (!await install.NamespaceExistsAsync(namespaceName))
            await install.CreateNamespaceAsync(namespaceName);

        var instance = await EnsureIdentityAsync(install, config, namespaceName);
        var manifests = new InstallManifestBuilder(config, instance);
        await RejectBindingCollisionAsync(install, namespaceName);

        if (config.ImagePullSecret is { } existingSecret)
        {
            if (!await install.SecretExistsAsync(existingSecret, namespaceName))
                throw new InstallBlockedException($"Secret {existingSecret} does not exist in namespace {namespaceName}.");
        }
        else
        {
            await client.ApplyAsync(manifests.PullSecret());
        }

        await client.ApplyAsync(manifests.Authorization());
        var existingConfig = await install.ReadConfigMapAsync(InstallManifestBuilder.ConfigName, namespaceName);
        string? existingJson = null;
        existingConfig?.Data?.TryGetValue("config.json", out existingJson);
        await client.ApplyAsync(manifests.Configuration(existingJson));

        await client.ApplyAsync(manifests.ServiceAccount());
        await client.ApplyAsync(manifests.Role());
        await client.ApplyAsync(manifests.RoleBinding());
        await client.ApplyAsync(manifests.ClusterRole());
        await client.ApplyAsync(manifests.ClusterRoleBinding());

        // A rerun must not undo the centrally selected Controller image.
        var existing = await install.ReadCronJobAsync(InstallManifestBuilder.ControllerName, namespaceName);
        var current = existing?.Spec?.JobTemplate?.Spec?.Template?.Spec?.Containers?.FirstOrDefault()?.Image;
        var image = recoverController || current is null ? manifests.LatestImage : current;
        await client.ApplyAsync(manifests.CronJob(image));
    }

    private static async Task RejectOperatorInstallationAsync(IKubernetesClient client, string namespaceName)
    {
        var operatorConfig = await client.Install.SecretExistsAsync(DataPlaneConstants.OperatorConfigSecretName, namespaceName);
        var pods = await client.CoreV1.ListNamespacedPodAsync(
            namespaceName,
            $"app.kubernetes.io/name={DataPlaneConstants.PulumiOperatorAppLabelValue}");
        if (operatorConfig || pods.Items?.Count > 0)
        {
            throw new InstallBlockedException(
                $"Namespace {namespaceName} holds an operator-managed installation; cn dp install does not migrate it. " +
                "Stop operator reconciliation, transfer state, project, stack and encryption, convert the configuration, and only then enable the Controller.");
        }
    }

    private static async Task<string> EnsureIdentityAsync(IKubernetesInstallClient install, InstallConfig config, string namespaceName)
    {
        var existing = await install.ReadConfigMapAsync(InstallManifestBuilder.IdentityName, namespaceName);
        if (existing is null)
        {
            var generated = Guid.NewGuid().ToString("D");
            var identity = new InstallManifestBuilder(config, generated).Identity();
            if (await install.TryCreateConfigMapAsync(identity, namespaceName))
                return generated;

            existing = await install.ReadConfigMapAsync(InstallManifestBuilder.IdentityName, namespaceName);
        }

        string? value = null;
        existing?.Data?.TryGetValue("clusterInstanceId", out value);
        if (value is null || !Uuid().IsMatch(value))
        {
            throw new InstallBlockedException(
                $"ConfigMap {InstallManifestBuilder.IdentityName} has no valid clusterInstanceId. Restore the original identity from backup.");
        }

        return value;
    }

    private static async Task RejectBindingCollisionAsync(IKubernetesInstallClient install, string namespaceName)
    {
        var binding = await install.ReadClusterRoleBindingAsync(InstallManifestBuilder.ControllerName);
        if (binding?.Subjects?.Any(subject => subject.NamespaceProperty != namespaceName) == true)
        {
            throw new InstallBlockedException(
                $"ClusterRoleBinding {InstallManifestBuilder.ControllerName} targets another namespace; only one Data Plane installation per cluster is supported.");
        }
    }
}
