using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>
/// Builds Pulumi stack manifests and related Kubernetes objects.
/// </summary>
internal sealed class PulumiStackManifestBuilder
{
    /// <summary>
    /// Default home directory for the Pulumi container.
    /// </summary>
    internal const string PulumiHomePath = "/home/pulumi";
    /// <summary>
    /// Default Pulumi backend path for local state.
    /// </summary>
    internal const string PulumiStatePath = "/pulumi-state";
    /// <summary>
    /// Default Pulumi S3 state prefix.
    /// </summary>
    internal const string PulumiS3StatePrefix = "pulumi";

    private readonly IPulumiOperatorProvisioner operatorProvisioner;
    private readonly string runtimeImage;
    private readonly string pluginsImage;

    /// <summary>
    /// Creates a manifest builder for Pulumi stack resources.
    /// </summary>
    public PulumiStackManifestBuilder(
        IPulumiOperatorProvisioner operatorProvisioner,
        IPulumiOperatorInfoProvider operatorInfoProvider)
    {
        this.operatorProvisioner = operatorProvisioner;
        var info = operatorInfoProvider.GetInfo();
        runtimeImage = info.RuntimeImage;
        pluginsImage = info.PluginsImage;
    }

    /// <summary>
    /// Builds the Pulumi stack custom resource manifest, including FluxCD wiring when enabled.
    /// </summary>
    /// <param name="config">Operator configuration values.</param>
    /// <param name="serviceAccountName">Service account used by the stack.</param>
    /// <returns>JSON manifest for the Pulumi stack.</returns>
    public JsonObject BuildStack(OperatorConfig config, string serviceAccountName)
    {
        var stackName = DataPlaneConstants.StackName;
        var image = BuildPulumiImage(config);
        var backendUrl = config.S3.Enabled
            ? $"s3://{config.S3.Bucket}/{PulumiS3StatePrefix}/{config.Environment.Name}?region={config.S3.Region}&endpoint={config.S3.Url}&s3ForcePathStyle=true"
            : $"file:///{PulumiStatePath}";

        var stack = new JsonObject
        {
            ["apiVersion"] = "pulumi.com/v1",
            ["kind"] = "Stack",
            ["metadata"] = new JsonObject
            {
                ["name"] = stackName,
                ["namespace"] = config.Kubernetes.Namespaces.System.Name,
            },
            ["spec"] = new JsonObject
            {
                ["serviceAccountName"] = serviceAccountName,
                ["stack"] = stackName,
                ["backend"] = backendUrl,
                ["envRefs"] = new JsonObject
                {
                    ["HOME"] = new JsonObject
                    {
                        ["literal"] = new JsonObject
                        {
                            ["value"] = PulumiHomePath
                        },
                        ["type"] = "Literal"
                    }
                    ,
                    // Disable DIY backend checkpoint backups (the `backups/` folder / `.bak` files).
                    // Always on; intentionally not configurable.
                    ["PULUMI_DIY_BACKEND_DISABLE_CHECKPOINT_BACKUPS"] = new JsonObject
                    {
                        ["literal"] = new JsonObject
                        {
                            ["value"] = "true"
                        },
                        ["type"] = "Literal"
                    }
                    ,
                    ["PULUMI_CONFIG_PASSPHRASE"] = new JsonObject
                    {
                        ["type"] = "Secret",
                        ["secret"] = new JsonObject
                        {
                            ["name"] = DataPlaneConstants.OperatorConfigSecretName,
                            ["key"] = DataPlaneConstants.DataPlaneConfigKeyPulumiPassphrase
                        }
                    }
                },
                ["destroyOnFinalize"] = true,
                ["retryOnUpdateConflict"] = true,
                ["workspaceTemplate"] = BuildPulumiWorkspaceTemplate(config, image)
            }
        };

        if (config.FluxCD?.Enabled != true)
        {
            stack.Set("spec.projectRepo", config.Scm.Url);
            stack.Set("spec.repoDir", config.Environment.Name);
            stack.Set("spec.resyncFrequencySeconds", DataPlaneConstants.ScmGitRepositorySyncIntervalSeconds);
            stack.Set("spec.branch", $"refs/heads/{DataPlaneConstants.ScmGitRepositoryDefaultBranch}");
        }
        else
        {
            // FluxCD provides sources; Pulumi stack references the GitRepository instead of direct SCM fields.
            stack.Set("spec.fluxSource.sourceRef.apiVersion", "source.toolkit.fluxcd.io/v1");
            stack.Set("spec.fluxSource.sourceRef.kind", "GitRepository");
            stack.Set("spec.fluxSource.sourceRef.name", FluxCDProvisioner.FluxcdGitRepositoryName);
            stack.Set("spec.fluxSource.dir", config.Environment.Name);
        }

        if (config.S3.Enabled && config.S3.AuthenticationMethod == S3AuthenticationMethod.AccessKeySecretKey)
        {
            var envRefs = JsonManifestEditor.EnsureObjectPath(stack, "spec.envRefs");
            envRefs["AWS_ACCESS_KEY_ID"] = new JsonObject
            {
                ["type"] = "Secret",
                ["secret"] = new JsonObject
                {
                    ["name"] = DataPlaneConstants.OperatorConfigSecretName,
                    ["key"] = DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessKey
                }
            };
            envRefs["AWS_SECRET_ACCESS_KEY"] = new JsonObject
            {
                ["type"] = "Secret",
                ["secret"] = new JsonObject
                {
                    ["name"] = DataPlaneConstants.OperatorConfigSecretName,
                    ["key"] = DataPlaneConstants.DataPlaneConfigKeyS3StorageSecretKey
                }
            };
        }

        ApplyPulumiStackSecrets(stack, config);
        ConfigurePulumiContainer(stack, config);
        ConfigureBootstrapContainer(stack, config);
        ConfigureFetchContainer(stack, config);
        ConfigureInstallPluginsContainer(stack, config);
        // Prune old DIY state history only for PVC-backed state; S3 should use bucket lifecycle policies.
        if (!config.S3.Enabled)
        {
            ConfigureHistoryPruneContainer(stack, config);
        }
        ApplyWorkspaceInputHash(stack, config);
        ApplyStackLabels(stack);
        ApplySystemLabelsToPath(stack, "spec.workspaceTemplate.spec.podTemplate.metadata");

        return stack;
    }

    /// <summary>
    /// Builds the Pulumi state PVC for local state storage.
    /// </summary>
    /// <param name="config">Operator configuration values.</param>
    /// <returns>PVC for local Pulumi state.</returns>
    public V1PersistentVolumeClaim BuildPulumiStatePvc(OperatorConfig config)
    {
        var pvc = new V1PersistentVolumeClaim
        {
            Metadata = new V1ObjectMeta
            {
                Name = DataPlaneConstants.PulumiStatePvcName,
                NamespaceProperty = config.Kubernetes.Namespaces.System.Name
            },
            Spec = new V1PersistentVolumeClaimSpec
            {
                StorageClassName = config.Kubernetes.StorageClass,
                AccessModes = ["ReadWriteOnce"],
                Resources = new V1VolumeResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity>
                    {
                        ["storage"] = new ResourceQuantity("1000Mi")
                    }
                }
            }
        };
        ApplyStackLabels(pvc.Metadata);

        return pvc;
    }

    /// <summary>
    /// Applies bootstrap labels to a Kubernetes object metadata instance.
    /// </summary>
    public void ApplyStackLabels(V1ObjectMeta? metadata)
    {
        KubernetesManifestTools.ApplyLabels(metadata, ProvisioningCommonTools.BootstrapLabels);
    }

    /// <summary>
    /// Applies bootstrap labels to a JSON object metadata path.
    /// </summary>
    public void ApplyStackLabels(JsonObject jsonObj)
    {
        KubernetesManifestTools.ApplyLabels(jsonObj, "metadata", ProvisioningCommonTools.BootstrapLabels);
    }

    /// <summary>
    /// Builds a JSON array from non-null nodes.
    /// </summary>
    private static JsonArray BuildJsonArray(params JsonNode?[] nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;

            array.Add((JsonNode)node);
        }
        return array;
    }

    /// <summary>
    /// Builds the workspace template used by the Pulumi stack.
    /// </summary>
    private JsonObject BuildPulumiWorkspaceTemplate(OperatorConfig config, string image)
    {
        var podSpec = new JsonObject
        {
            ["automountServiceAccountToken"] = false,
            ["terminationGracePeriodSeconds"] = 3600,
            ["securityContext"] = new JsonObject
            {
                ["runAsUser"] = config.Kubernetes.SecurityContextRunAsId,
                ["runAsGroup"] = config.Kubernetes.SecurityContextRunAsId,
                ["fsGroup"] = config.Kubernetes.SecurityContextRunAsId
            },
            ["imagePullSecrets"] = BuildJsonArray(
                new JsonObject
                {
                    ["name"] = KubernetesConstants.SystemImagePullSecret
                }),

            ["containers"] = BuildJsonArray(
                new JsonObject
                {
                    ["name"] = "pulumi",
                    ["volumeMounts"] = BuildJsonArray(
                        ProvisioningCommonTools.BuildServiceAccountVolumeMount())
                }),
            ["volumes"] = BuildJsonArray(
                ProvisioningCommonTools.BuildServiceAccountVolume())

        };

        ProvisioningCommonTools.ApplySystemNodePlacement(podSpec, config);

        var workspaceTemplate = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["labels"] = new JsonObject()
            },
            ["spec"] = new JsonObject
            {
                ["resources"] = new JsonObject
                {
                    ["requests"] = new JsonObject
                    {
                        ["memory"] = "500Mi",
                        ["cpu"] = "500m"
                    },
                    ["limits"] = new JsonObject
                    {
                        ["memory"] = "2000Mi",
                        ["cpu"] = "1000m"
                    }
                },
                ["image"] = image,
                ["podTemplate"] = new JsonObject
                {
                    ["spec"] = podSpec
                }
            }
        };

        ApplyStackLabels(workspaceTemplate);
        return workspaceTemplate;
    }

    /// <summary>
    /// Applies bootstrap labels to the given metadata path.
    /// </summary>
    /// <summary>
    /// Applies bootstrap labels to the given metadata path.
    /// </summary>
    private static void ApplySystemLabelsToPath(JsonObject root, string path)
    {
        KubernetesManifestTools.ApplyLabels(root, path, ProvisioningCommonTools.BootstrapLabels);
    }

    private static void ApplyWorkspaceInputHash(JsonObject stack, OperatorConfig config)
    {
        var annotations = JsonManifestEditor.EnsureObjectPath(
            stack,
            "spec.workspaceTemplate.spec.podTemplate.metadata.annotations");
        annotations[DataPlaneConstants.WorkspaceInputHashAnnotation] = BuildWorkspaceInputHash(config);
    }

    private static string BuildWorkspaceInputHash(OperatorConfig config)
    {
        var secretData = new DataPlaneConfigSecretBuilder().Build(config).Data
            ?? new Dictionary<string, byte[]>();
        var sb = new StringBuilder();

        void AddValue(string key, object? value)
        {
            sb.Append(key);
            sb.Append('\0');
            sb.Append(value?.ToString() ?? string.Empty);
            sb.Append('\n');
        }

        AddValue("environment.name", config.Environment.Name);
        AddValue("scm.url", config.Scm.Url);
        AddValue("scm.authenticationMethod", config.Scm.AuthenticationMethod);
        AddValue("containerRegistry.hostname", config.ContainerRegistry.Hostname);
        AddValue("kubernetes.securityContextRunAsId", config.Kubernetes.SecurityContextRunAsId);
        AddValue("kubernetes.podPlacementMode", config.Kubernetes.PodPlacementMode);
        AddValue("pulumi.images.runtimeVersion", config.Pulumi.Images.RuntimeVersion);
        AddValue("pulumi.images.pluginsVersion", config.Pulumi.Images.PluginsVersion);
        AddValue("s3.enabled", config.S3.Enabled);
        AddValue("s3.url", config.S3.Url);
        AddValue("s3.bucket", config.S3.Bucket);
        AddValue("s3.region", config.S3.Region);
        AddValue("s3.authenticationMethod", config.S3.AuthenticationMethod);
        AddValue("fluxcd.enabled", config.FluxCD?.Enabled);

        foreach (var (key, value) in secretData.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            sb.Append("secret:");
            sb.Append(key);
            sb.Append('\0');
            sb.Append(Convert.ToBase64String(value));
            sb.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// Populates secret references, SCM auth (when FluxCD is disabled), and environment metadata for the stack.
    /// </summary>
    private static void ApplyPulumiStackSecrets(JsonObject stack, OperatorConfig config)
    {
        if (stack["spec"] is not JsonObject spec)
            return;

        var secretName = DataPlaneConstants.OperatorConfigSecretName;
        var secretsRef = JsonManifestEditor.EnsureObjectPath(spec, "secretsRef");
        var scm = config.Scm;
        var s3AccessKey = string.Empty;
        var s3SecretKey = string.Empty;
        var s3IamRole = string.Empty;
        var s3Region = string.Empty;
        if (config.S3.Enabled)
        {
            s3Region = config.S3.Region ?? string.Empty;
            if (config.S3.AuthenticationMethod == S3AuthenticationMethod.AccessKeySecretKey)
            {
                s3AccessKey = config.S3.AccessKey ?? string.Empty;
                s3SecretKey = config.S3.SecretKey ?? string.Empty;
            }
            else
            {
                s3IamRole = config.S3.IAMRole ?? string.Empty;
            }
        }
        var podPlacementMode = PodPlacementModeExtensions.ToConfigString(config.Kubernetes.PodPlacementMode);

        string FormatEnvName(string key)
        {
            var formatted = key.Replace('_', '.');
            return formatted.StartsWith("cn.", StringComparison.Ordinal)
                ? "cn:" + formatted["cn.".Length..]
                : formatted;
        }

        void AddSecretEnvRef(string key)
        {
            var envName = FormatEnvName(key);
            secretsRef[envName] = new JsonObject
            {
                ["type"] = "Secret",
                ["secret"] = new JsonObject
                {
                    ["name"] = secretName,
                    ["key"] = key
                }
            };
        }

        if (config.FluxCD?.Enabled != true)
        {
            if (scm.AuthenticationMethod == ScmAuthenticationMethod.AccessToken)
            {
                spec["gitAuth"] = new JsonObject
                {
                    ["accessToken"] = new JsonObject
                    {
                        ["type"] = "Secret",
                        ["secret"] = new JsonObject
                        {
                            ["name"] = secretName,
                            ["key"] = DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthToken
                        }
                    }
                };
            }

            if (scm.AuthenticationMethod == ScmAuthenticationMethod.UsernamePassword)
            {
                spec["gitAuth"] = new JsonObject
                {
                    ["basicAuth"] = new JsonObject
                    {
                        ["password"] = new JsonObject
                        {
                            ["type"] = "Secret",
                            ["secret"] = new JsonObject
                            {
                                ["name"] = secretName,
                                ["key"] = DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthPassword
                            }
                        },
                        ["userName"] = new JsonObject
                        {
                            ["type"] = "Secret",
                            ["secret"] = new JsonObject
                            {
                                ["name"] = secretName,
                                ["key"] = DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthUsername
                            }
                        }
                    }
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(config.Scm.AccessToken))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthToken);
        if (!string.IsNullOrWhiteSpace(config.Scm.Password))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthPassword);
        if (!string.IsNullOrWhiteSpace(config.Scm.Username))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyScmSystemAuthUsername);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert);
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.Namespaces.System.Name))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyTargetNamespace);
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.Namespaces.Cni.Name))
        {
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyCniDedicatedNamespaceName);
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyCniDedicatedNamespaceEnabled);
        }
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.Namespaces.CiPipelines.Name))
        {
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyCiPipelinesDedicatedNamespaceName);
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyCiPipelinesDedicatedNamespaceEnabled);
        }
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.NodeLabels.System.Key))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyNodePlacementSystemNodeLabelKey);
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.NodeLabels.System.Value))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyNodePlacementSystemNodeLabelValue);
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.NodeLabels.Application.Key))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyNodePlacementApplicationNodeLabelKey);
        if (!string.IsNullOrWhiteSpace(config.Kubernetes.NodeLabels.Application.Value))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyNodePlacementApplicationNodeLabelValue);
        if (!string.IsNullOrWhiteSpace(config.ContainerRegistry.Username))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyContainerRegistrySystemUsername);
        if (!string.IsNullOrWhiteSpace(config.ContainerRegistry.Password))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyContainerRegistrySystemPassword);
        AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyHttpProxyEnabled);
        if (!string.IsNullOrWhiteSpace(config.HttpProxy.Hostname))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyHttpProxyHostname);
        if (config.HttpProxy.Port.HasValue)
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyHttpProxyPort);
        if (!string.IsNullOrWhiteSpace(config.HttpProxy.NoProxy))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyHttpProxyNoProxyHostnames);
        AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyS3Enabled);
        if (!string.IsNullOrWhiteSpace(s3AccessKey))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessKey);
        if (!string.IsNullOrWhiteSpace(s3SecretKey))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyS3StorageSecretKey);
        if (!string.IsNullOrWhiteSpace(s3IamRole))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyS3StorageAccessRole);
        if (!string.IsNullOrWhiteSpace(s3Region))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyS3StorageRegion);
        if (!string.IsNullOrWhiteSpace(podPlacementMode))
            AddSecretEnvRef(DataPlaneConstants.DataPlaneConfigKeyNodePlacementMode);
    }

    /// <summary>
    /// Configures the main Pulumi container and shared volumes.
    /// </summary>
    private void ConfigurePulumiContainer(JsonObject stack, OperatorConfig config)
    {
        var podSpec = JsonManifestEditor.EnsureObjectPath(stack, "spec.workspaceTemplate.spec.podTemplate.spec");

        var pulumiContainer = JsonManifestEditor.EnsureNamedObject(
            JsonManifestEditor.EnsureArray(podSpec, "containers"),
            "pulumi");

        pulumiContainer["securityContext"] = new JsonObject
        {
            ["readOnlyRootFilesystem"] = true,
            ["allowPrivilegeEscalation"] = false
        };

        var volumeMounts = JsonManifestEditor.EnsureArray(pulumiContainer, "volumeMounts");
        ProvisioningCommonTools.EnsureVolumeMount(volumeMounts, "tmp", "/tmp", null, false);
        ProvisioningCommonTools.EnsureVolumeMount(volumeMounts, "npmrc", $"{PulumiHomePath}/.npmrc", ".npmrc", true);
        ProvisioningCommonTools.EnsureVolumeMount(volumeMounts, "pulumi", PulumiHomePath, null, false);
        if (!config.S3.Enabled)
            ProvisioningCommonTools.EnsureVolumeMount(volumeMounts, "pulumi-state", PulumiStatePath, null, false);
        ProvisioningCommonTools.EnsureVolumeMount(
            volumeMounts,
            "docker-auth",
            $"{PulumiHomePath}/.docker/config.json",
            "config.json",
            true);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            ProvisioningCommonTools.EnsureVolumeMount(
                volumeMounts,
                "ca-certificates",
                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert,
                true);
            var env = JsonManifestEditor.EnsureArray(pulumiContainer, "env");
            JsonManifestEditor.EnsureEnvVar(
                env,
                "NODE_EXTRA_CA_CERTS",
                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}");
        }
        ProvisioningCommonTools.EnsureProxyEnv(pulumiContainer, config);
        var volumes = JsonManifestEditor.EnsureArray(podSpec, "volumes");
        ProvisioningCommonTools.EnsureSecretVolume(volumes, "npmrc", DataPlaneConstants.OperatorConfigSecretName);
        EnsureEmptyDirVolume(volumes, "pulumi");
        EnsureEmptyDirVolume(volumes, "tmp");
        if (!config.S3.Enabled)
            EnsurePersistentVolumeClaimVolume(volumes, "pulumi-state", DataPlaneConstants.PulumiStatePvcName);
        ProvisioningCommonTools.EnsureSecretVolume(volumes, "docker-auth", DataPlaneConstants.OperatorConfigSecretName);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            ProvisioningCommonTools.EnsureSecretVolumeWithItem(
                volumes,
                "ca-certificates",
                DataPlaneConstants.OperatorConfigSecretName,
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert);
        }
    }

    /// <summary>
    /// Configures the bootstrap init container.
    /// </summary>
    private void ConfigureBootstrapContainer(JsonObject stack, OperatorConfig config)
    {
        ConfigureInitContainer(stack, config, "bootstrap");
    }

    /// <summary>
    /// Configures the fetch init container used for source checkout.
    /// </summary>
    private void ConfigureFetchContainer(JsonObject stack, OperatorConfig config)
    {
        ConfigureInitContainer(stack, config, "fetch");
        var podSpec = JsonManifestEditor.EnsureObjectPath(stack, "spec.workspaceTemplate.spec.podTemplate.spec");
        var initContainers = JsonManifestEditor.EnsureArray(podSpec, "initContainers");
        var container = JsonManifestEditor.FindByName(initContainers, "fetch");
        if (container is null)
            return;

        var volumeMounts = JsonManifestEditor.EnsureArray(container, "volumeMounts");
        ProvisioningCommonTools.EnsureVolumeMount(volumeMounts, "tmp", "/tmp", null, false);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            ProvisioningCommonTools.EnsureVolumeMount(
                volumeMounts,
                "ca-certificates",
                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert,
                true);
        }
        ProvisioningCommonTools.EnsureProxyEnv(container, config);
    }

    /// <summary>
    /// Configures the init container that installs Pulumi plugins.
    /// </summary>
    private void ConfigureInstallPluginsContainer(JsonObject stack, OperatorConfig config)
    {
        var podSpec = JsonManifestEditor.EnsureObjectPath(stack, "spec.workspaceTemplate.spec.podTemplate.spec");
        var initContainers = JsonManifestEditor.EnsureArray(podSpec, "initContainers");
        var container = JsonManifestEditor.FindByName(initContainers, "install-plugins");
        if (container is null)
        {
            container = new JsonObject
            {
                ["name"] = "install-plugins"
            };
            initContainers.Add((JsonNode)container);
        }

        container["securityContext"] = new JsonObject
        {
            ["readOnlyRootFilesystem"] = true,
            ["allowPrivilegeEscalation"] = false
        };
        container["image"] = BuildPulumiOperatorPluginsImage(config);
        container["resources"] = new JsonObject
        {
            ["limits"] = new JsonObject
            {
                ["memory"] = "128Mi",
                ["cpu"] = "200m"
            }
        };
        container["command"] = BuildJsonArray(
            JsonValue.Create("sh"),
            JsonValue.Create("-c"),
            JsonValue.Create(
                $"mkdir -p {PulumiHomePath}/.pulumi/plugins\n" +
                $"cp -f -r /data/.pulumi/plugins/. {PulumiHomePath}/.pulumi/plugins/\n"));
        container["volumeMounts"] = BuildJsonArray(
            new JsonObject
            {
                ["name"] = "pulumi",
                ["mountPath"] = PulumiHomePath
            });
    }

    /// <summary>
    /// Configures an init container that prunes old DIY state history entries on the state PVC,
    /// retaining only the most recent <see cref="DataPlaneConstants.PulumiStateHistoryRetainCount"/>.
    /// Only the history subdirectory is mounted (via subPath), so the container cannot touch any
    /// other state files. Invoked only for PVC-backed state.
    /// </summary>
    private void ConfigureHistoryPruneContainer(JsonObject stack, OperatorConfig config)
    {
        const string historyMountPath = "/history";

        var podSpec = JsonManifestEditor.EnsureObjectPath(stack, "spec.workspaceTemplate.spec.podTemplate.spec");
        var initContainers = JsonManifestEditor.EnsureArray(podSpec, "initContainers");
        var container = JsonManifestEditor.FindByName(initContainers, "prune-history");
        if (container is null)
        {
            container = new JsonObject
            {
                ["name"] = "prune-history"
            };
            initContainers.Add((JsonNode)container);
        }

        container["image"] = BuildPulumiImage(config);
        container["securityContext"] = new JsonObject
        {
            ["readOnlyRootFilesystem"] = true,
            ["allowPrivilegeEscalation"] = false
        };
        container["resources"] = new JsonObject
        {
            ["limits"] = new JsonObject
            {
                ["memory"] = "128Mi",
                ["cpu"] = "200m"
            }
        };

        // Keep the newest N history entries; delete the rest. Each entry is four files:
        // <base>.history.json (+ .attrs) and <base>.checkpoint.json (+ .attrs) — remove all of them.
        // No-op when the history directory does not yet exist (fresh PVC).
        var keepFrom = DataPlaneConstants.PulumiStateHistoryRetainCount + 1;
        container["command"] = BuildJsonArray(
            JsonValue.Create("sh"),
            JsonValue.Create("-c"),
            JsonValue.Create(
                $"set -eu\n" +
                $"HIST={historyMountPath}\n" +
                "[ -d \"$HIST\" ] || exit 0\n" +
                "find \"$HIST\" -name '*.history.json' -printf '%T@ %p\\n' " +
                $"| sort -rn | tail -n +{keepFrom} | cut -d' ' -f2- " +
                "| while read -r f; do b=\"${f%.history.json}\"; " +
                "rm -f \"$b.history.json\" \"$b.history.json.attrs\" " +
                "\"$b.checkpoint.json\" \"$b.checkpoint.json.attrs\"; done\n"));

        // Mount only the history subdirectory of the state PVC, so this container is physically
        // unable to touch state.json, stacks, or backups.
        container["volumeMounts"] = BuildJsonArray(
            new JsonObject
            {
                ["name"] = "pulumi-state",
                ["mountPath"] = historyMountPath,
                ["subPath"] = ".pulumi/history"
            });
    }

    /// <summary>
    /// Initializes common settings for named init containers.
    /// </summary>
    private void ConfigureInitContainer(JsonObject stack, OperatorConfig config, string name)
    {
        var podSpec = JsonManifestEditor.EnsureObjectPath(stack, "spec.workspaceTemplate.spec.podTemplate.spec");
        var initContainers = JsonManifestEditor.EnsureArray(podSpec, "initContainers");
        var container = JsonManifestEditor.FindByName(initContainers, name);
        if (container is null)
        {
            container = new JsonObject
            {
                ["name"] = name
            };
            initContainers.Add((JsonNode)container);
        }

        container["image"] = operatorProvisioner.GetOperatorImage(config);
        container["securityContext"] = new JsonObject
        {
            ["readOnlyRootFilesystem"] = true,
            ["allowPrivilegeEscalation"] = false
        };
        container["resources"] = new JsonObject
        {
            ["limits"] = new JsonObject
            {
                ["memory"] = "512Mi",
                ["cpu"] = "500m"
            }
        };
    }

    /// <summary>
    /// Ensures an emptyDir volume exists.
    /// </summary>
    private static void EnsureEmptyDirVolume(JsonArray volumes, string name)
    {
        if (JsonManifestEditor.HasNamedObject(volumes, name))
            return;

        volumes.Add((JsonNode)new JsonObject
        {
            ["name"] = name,
            ["emptyDir"] = new JsonObject()
        });
    }

    /// <summary>
    /// Ensures a persistent volume claim volume exists.
    /// </summary>
    private static void EnsurePersistentVolumeClaimVolume(
        JsonArray volumes,
        string name,
        string claimName)
    {
        if (JsonManifestEditor.HasNamedObject(volumes, name))
            return;

        volumes.Add((JsonNode)new JsonObject
        {
            ["name"] = name,
            ["persistentVolumeClaim"] = new JsonObject
            {
                ["claimName"] = claimName
            }
        });
    }

    /// <summary>
    /// Builds the Pulumi runtime image reference.
    /// </summary>
    private string BuildPulumiImage(OperatorConfig config)
    {
        var baseImage = $"{runtimeImage}:{config.Pulumi.Images.RuntimeVersion}";
        if (string.IsNullOrWhiteSpace(config.ContainerRegistry.Hostname))
            return baseImage;

        return $"{config.ContainerRegistry.Hostname.TrimEnd('/')}/{baseImage}";
    }

    /// <summary>
    /// Builds the Pulumi plugins image reference.
    /// </summary>
    private string BuildPulumiOperatorPluginsImage(OperatorConfig config)
    {
        var baseImage = pluginsImage;
        if (!string.IsNullOrWhiteSpace(config.Pulumi.Images.PluginsVersion))
            baseImage = $"{baseImage}:{config.Pulumi.Images.PluginsVersion}";

        if (string.IsNullOrWhiteSpace(config.ContainerRegistry.Hostname))
            return baseImage;

        return $"{config.ContainerRegistry.Hostname.TrimEnd('/')}/{baseImage}";
    }

}
