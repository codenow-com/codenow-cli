using System.Text;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Builds the operator configuration secret data from configuration.
/// </summary>
internal sealed class DataPlaneConfigSecretBuilder
{
    /// <summary>
    /// Builds the Kubernetes secret for operator configuration.
    /// </summary>
    /// <param name="config">Operator configuration values.</param>
    /// <returns>Secret populated with configuration data.</returns>
    public V1Secret Build(OperatorConfig config)
    {
        var token = config.Scm.AccessToken;
        var username = config.Scm.Username;
        var password = config.Scm.Password;
        var passphrase = config.Pulumi.Passphrase;
        var systemNamespace = config.Kubernetes.Namespaces.System.Name;
        var cniNamespace = config.Kubernetes.Namespaces.Cni.Name;
        var ciPipelinesNamespace = config.Kubernetes.Namespaces.CiPipelines.Name;
        var systemNodeLabelKey = config.Kubernetes.NodeLabels.System.Key;
        var systemNodeLabelValue = config.Kubernetes.NodeLabels.System.Value;
        var appNodeLabelKey = config.Kubernetes.NodeLabels.Application.Key;
        var appNodeLabelValue = config.Kubernetes.NodeLabels.Application.Value;
        var containerRegistryUsername = config.ContainerRegistry.Username;
        var containerRegistryPass = config.ContainerRegistry.Password;
        var containerRegistryHostname = config.ContainerRegistry.Hostname;
        var httpProxyEnabled = config.HttpProxy.Enabled;
        var httpProxyHostname = config.HttpProxy.Hostname;
        var httpProxyPort = config.HttpProxy.Port;
        var httpProxyNoProxyHostnames = config.HttpProxy.NoProxy;
        var customCaBase64 = config.Security.CustomCaBase64;
        var s3AccessKey = string.Empty;
        var s3SecretKey = string.Empty;
        var s3IamRole = string.Empty;
        var s3Region = string.Empty;
        var s3Enabled = config.S3.Enabled;
        if (s3Enabled)
        {
            s3Region = config.S3.Region;
            if (config.S3.AuthenticationMethod == S3AuthenticationMethod.AccessKeySecretKey)
            {
                s3AccessKey = config.S3.AccessKey;
                s3SecretKey = config.S3.SecretKey;
            }
            else
            {
                s3IamRole = config.S3.IAMRole;
            }
        }
        var podPlacementMode = PodPlacementModeExtensions.ToConfigString(config.Kubernetes.PodPlacementMode);

        var data = new Dictionary<string, byte[]>();

        if (!string.IsNullOrWhiteSpace(token))
            data[DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthToken] = Encoding.UTF8.GetBytes(token);
        if (!string.IsNullOrWhiteSpace(password))
            data[DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthPassword] = Encoding.UTF8.GetBytes(password);
        if (!string.IsNullOrWhiteSpace(username))
            data[DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthUsername] = Encoding.UTF8.GetBytes(username);
        if (!string.IsNullOrWhiteSpace(passphrase))
            data[DataPlaneConstants.DataPlaneConfigKeyPulumiPassphrase] = Encoding.UTF8.GetBytes(passphrase);
        if (!string.IsNullOrWhiteSpace(customCaBase64))
            data[DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert] = Convert.FromBase64String(customCaBase64);
        if (!string.IsNullOrWhiteSpace(systemNamespace))
            data[DataPlaneConstants.DataPlaneConfigKeyTargetNamespace] = Encoding.UTF8.GetBytes(systemNamespace);
        if (!string.IsNullOrWhiteSpace(cniNamespace))
            data[DataPlaneConstants.DataPlaneConfigKeyCniDedicatedNamespaceName] = Encoding.UTF8.GetBytes(cniNamespace);
        if (!string.IsNullOrWhiteSpace(ciPipelinesNamespace))
            data[DataPlaneConstants.DataPlaneConfigKeyCiPipelinesDedicatedNamespaceName] =
                Encoding.UTF8.GetBytes(ciPipelinesNamespace);
        data[DataPlaneConstants.DataPlaneConfigKeyCniDedicatedNamespaceEnabled] =
            Encoding.UTF8.GetBytes(
                config.Kubernetes.Namespaces.Cni.IsDedicatedRelativeTo(config.Kubernetes.Namespaces.System)
                    ? "true"
                    : "false");
        data[DataPlaneConstants.DataPlaneConfigKeyCiPipelinesDedicatedNamespaceEnabled] =
            Encoding.UTF8.GetBytes(
                config.Kubernetes.Namespaces.CiPipelines.IsDedicatedRelativeTo(config.Kubernetes.Namespaces.System)
                    ? "true"
                    : "false");
        if (!string.IsNullOrWhiteSpace(systemNodeLabelKey))
            data[DataPlaneConstants.DataPlaneConfigKeyNodePlacementSystemNodeLabelKey] =
                Encoding.UTF8.GetBytes(systemNodeLabelKey);
        if (!string.IsNullOrWhiteSpace(systemNodeLabelValue))
            data[DataPlaneConstants.DataPlaneConfigKeyNodePlacementSystemNodeLabelValue] =
                Encoding.UTF8.GetBytes(systemNodeLabelValue);
        if (!string.IsNullOrWhiteSpace(appNodeLabelKey))
            data[DataPlaneConstants.DataPlaneConfigKeyNodePlacementApplicationNodeLabelKey] =
                Encoding.UTF8.GetBytes(appNodeLabelKey);
        if (!string.IsNullOrWhiteSpace(appNodeLabelValue))
            data[DataPlaneConstants.DataPlaneConfigKeyNodePlacementApplicationNodeLabelValue] =
                Encoding.UTF8.GetBytes(appNodeLabelValue);
        if (!string.IsNullOrWhiteSpace(containerRegistryUsername))
            data[DataPlaneConstants.DataPlaneConfigKeyContainerRegistrySystemUsername] =
                Encoding.UTF8.GetBytes(containerRegistryUsername);
        if (!string.IsNullOrWhiteSpace(containerRegistryPass))
            data[DataPlaneConstants.DataPlaneConfigKeyContainerRegistrySystemPassword] =
                Encoding.UTF8.GetBytes(containerRegistryPass);
        if (!string.IsNullOrWhiteSpace(containerRegistryHostname) &&
            !string.IsNullOrWhiteSpace(containerRegistryUsername) &&
            !string.IsNullOrWhiteSpace(containerRegistryPass))
        {
            var containerRegistryAuth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{containerRegistryUsername}:{containerRegistryPass}"));
            var containerRegistryConfigJson =
                $"{{\"auths\":{{\"{containerRegistryHostname}\":{{\"auth\":\"{containerRegistryAuth}\"}}}}}}";
            data[DataPlaneConstants.DataPlaneConfigKeyContainerRegistryConfigJson] =
                Encoding.UTF8.GetBytes(containerRegistryConfigJson);
        }
        data[DataPlaneConstants.DataPlaneConfigKeyHttpProxyEnabled] =
            httpProxyEnabled ? Encoding.UTF8.GetBytes("true") : Encoding.UTF8.GetBytes("false");
        if (!string.IsNullOrWhiteSpace(httpProxyHostname))
            data[DataPlaneConstants.DataPlaneConfigKeyHttpProxyHostname] = Encoding.UTF8.GetBytes(httpProxyHostname);
        if (httpProxyPort.HasValue)
            data[DataPlaneConstants.DataPlaneConfigKeyHttpProxyPort] =
                Encoding.UTF8.GetBytes(httpProxyPort.Value.ToString());
        if (!string.IsNullOrWhiteSpace(httpProxyNoProxyHostnames))
            data[DataPlaneConstants.DataPlaneConfigKeyHttpProxyNoProxyHostnames] =
                Encoding.UTF8.GetBytes(httpProxyNoProxyHostnames);
        data[DataPlaneConstants.DataPlaneConfigKeyS3Enabled] =
            s3Enabled ? Encoding.UTF8.GetBytes("true") : Encoding.UTF8.GetBytes("false");
        if (!string.IsNullOrWhiteSpace(s3AccessKey))
            data[DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessKey] = Encoding.UTF8.GetBytes(s3AccessKey);
        if (!string.IsNullOrWhiteSpace(s3SecretKey))
            data[DataPlaneConstants.DataPlaneConfigKeyS3StorageSecretKey] = Encoding.UTF8.GetBytes(s3SecretKey);
        if (!string.IsNullOrWhiteSpace(s3IamRole))
            data[DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessRole] = Encoding.UTF8.GetBytes(s3IamRole);
        if (!string.IsNullOrWhiteSpace(s3Region))
            data[DataPlaneConstants.DataPlaneConfigKeyS3StorageRegion] = Encoding.UTF8.GetBytes(s3Region);
        if (!string.IsNullOrWhiteSpace(podPlacementMode))
            data[DataPlaneConstants.DataPlaneConfigKeyNodePlacementMode] = Encoding.UTF8.GetBytes(podPlacementMode);

        var npmrc = BuildNpmrc(config.NpmRegistry);
        data[DataPlaneConstants.DataPlaneConfigKeyNpmrc] = Encoding.UTF8.GetBytes(npmrc);

        return new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = DataPlaneConstants.PulumiOperatorConfigSecretName,
                NamespaceProperty = config.Kubernetes.Namespaces.System.Name
            },
            Type = "Opaque",
            Data = data
        };
    }

    private static string BuildNpmrc(NpmRegistryConfig npmRegistry)
    {
        if (string.IsNullOrWhiteSpace(npmRegistry.Url) ||
            string.IsNullOrWhiteSpace(npmRegistry.AccessToken))
            return string.Empty;

        var registryUrl = npmRegistry.Url.Trim();
        if (!registryUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            registryUrl = $"https://{registryUrl.TrimStart('/')}";

        var authHost = registryUrl;
        if (Uri.TryCreate(registryUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            registryUrl = uri.GetLeftPart(UriPartial.Authority) + path;
            authHost = uri.Host + path;
        }

        return $"registry={registryUrl}\n//{authHost}/:_authToken={npmRegistry.AccessToken}\n";
    }
}
