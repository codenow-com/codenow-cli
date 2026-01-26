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

    private readonly IPulumiOperatorProvisioner operatorProvisioner;
    private readonly DataPlaneConfigSecretBuilder configSecretBuilder;
    private readonly string runtimeImage;
    private readonly string pluginsImage;

    /// <summary>
    /// Creates a manifest builder for Pulumi stack resources.
    /// </summary>
    public PulumiStackManifestBuilder(
        IPulumiOperatorProvisioner operatorProvisioner,
        DataPlaneConfigSecretBuilder configSecretBuilder,
        IPulumiOperatorInfoProvider operatorInfoProvider)
    {
        this.operatorProvisioner = operatorProvisioner;
        this.configSecretBuilder = configSecretBuilder;
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
            ? config.S3.Url
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
        ApplyStackLabels(stack);
        ApplySystemLabelsToPath(stack, "spec.workspaceTemplate.spec.podTemplate.metadata");

        return stack;
    }

    /// <summary>
    /// Builds the operator configuration secret.
    /// </summary>
    /// <param name="config">Operator configuration values.</param>
    /// <returns>Kubernetes secret object.</returns>
    public V1Secret BuildDataPlaneConfigSecret(OperatorConfig config)
    {
        var secret = configSecretBuilder.Build(config);
        ApplyStackLabels(secret.Metadata);

        return secret;
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
                        ["memory"] = "1500Mi",
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
        EnsureVolumeMount(volumeMounts, "tmp", "/tmp", null, false);
        EnsureVolumeMount(volumeMounts, "npmrc", $"{PulumiHomePath}/.npmrc", ".npmrc", true);
        EnsureVolumeMount(volumeMounts, "pulumi", PulumiHomePath, null, false);
        if (!config.S3.Enabled)
            EnsureVolumeMount(volumeMounts, "pulumi-state", PulumiStatePath, null, false);
        EnsureVolumeMount(
            volumeMounts,
            "docker-auth",
            $"{PulumiHomePath}/.docker/config.json",
            "config.json",
            true);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            EnsureVolumeMount(
                volumeMounts,
                "ca-certificates",
                $"/etc/ssl/certs/{DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert}",
                DataPlaneConstants.DataPlaneConfigKeyPkiCustomCaCert,
                true);
        }
        ProvisioningCommonTools.EnsureProxyEnv(pulumiContainer, config);
        var volumes = JsonManifestEditor.EnsureArray(podSpec, "volumes");
        EnsureSecretVolume(volumes, "npmrc", DataPlaneConstants.OperatorConfigSecretName);
        EnsureEmptyDirVolume(volumes, "pulumi");
        EnsureEmptyDirVolume(volumes, "tmp");
        if (!config.S3.Enabled)
            EnsurePersistentVolumeClaimVolume(volumes, "pulumi-state", DataPlaneConstants.PulumiStatePvcName);
        EnsureSecretVolume(volumes, "docker-auth", DataPlaneConstants.OperatorConfigSecretName);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {
            EnsureSecretVolumeWithItem(
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
        EnsureVolumeMount(volumeMounts, "tmp", "/tmp", null, false);
        if (!string.IsNullOrWhiteSpace(config.Security.CustomCaBase64))
        {  
            EnsureVolumeMount(
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
    /// Ensures a volume mount exists on the container.
    /// </summary>
    private static void EnsureVolumeMount(
        JsonArray volumeMounts,
        string name,
        string mountPath,
        string? subPath,
        bool readOnly)
    {
        if (JsonManifestEditor.HasNamedObject(volumeMounts, name))
            return;

        var mount = new JsonObject
        {
            ["name"] = name,
            ["mountPath"] = mountPath
        };
        if (!string.IsNullOrWhiteSpace(subPath))
            mount["subPath"] = subPath;
        if (readOnly)
            mount["readOnly"] = true;

        volumeMounts.Add((JsonNode)mount);
    }

    /// <summary>
    /// Ensures a secret volume exists.
    /// </summary>
    private static void EnsureSecretVolume(JsonArray volumes, string name, string secretName)
    {
        if (JsonManifestEditor.HasNamedObject(volumes, name))
            return;

        volumes.Add((JsonNode)new JsonObject
        {
            ["name"] = name,
            ["secret"] = new JsonObject
            {
                ["secretName"] = secretName
            }
        });
    }

    /// <summary>
    /// Ensures a secret volume exists with a single item mapping.
    /// </summary>
    private static void EnsureSecretVolumeWithItem(
        JsonArray volumes,
        string name,
        string secretName,
        string key)
    {
        if (JsonManifestEditor.HasNamedObject(volumes, name))
            return;

        volumes.Add((JsonNode)new JsonObject
        {
            ["name"] = name,
            ["secret"] = new JsonObject
            {
                ["secretName"] = secretName,
                ["items"] = BuildJsonArray(
                    new JsonObject
                    {
                        ["key"] = key,
                        ["path"] = key
                    })
            }
        });
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
