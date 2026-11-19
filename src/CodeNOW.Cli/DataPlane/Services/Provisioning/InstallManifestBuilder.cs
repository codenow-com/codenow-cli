using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>Builds the Installer-owned resources. Pure: no cluster access.</summary>
public sealed class InstallManifestBuilder(InstallConfig config, string clusterInstanceId)
{
    public const string ControllerName = "cn-dp-update-controller";
    public const string ConfigName = "cn-dp-update-controller-config";
    public const string IdentityName = "cn-dp-identity";
    public const string AuthorizationName = "cn-dp-authorization";
    public const string PullSecretName = "cn-system-image-pull-secret";
    public const string ControllerRepository = "codenow/data-plane-update-controller";
    public const string Schedule = "*/5 * * * *";
    public const string DeployerName = "cn-dp-deployer";

    /// <summary>Kubernetes deadline of one Controller Job, matching the Controller's 900-second run budget.</summary>
    public const long RunDeadlineSeconds = 900;

    /// <summary>Pod termination grace of one Controller Job.</summary>
    public const long TerminationGraceSeconds = 30;

    private const string RbacGroup = "rbac.authorization.k8s.io";
    private const string ProfileResource = "DataPlane/Manifests/UpdateController/deployer-profile.json";
    /// <summary>UID, GID and fsGroup of both Job types (data-plane-installation spec, Restricted admission).</summary>
    private const long NonRootId = 1000;

    private string Namespace => config.NamespaceName!;

    /// <summary>
    /// Name of the Controller ClusterRole and ClusterRoleBinding. Cluster-scoped names end with the
    /// installation namespace, so installations never share a cluster-scoped resource.
    /// </summary>
    public static string ClusterScopedName(string namespaceName) => $"{ControllerName}-{namespaceName}";

    /// <summary>Name of the Deployer ClusterRole and ClusterRoleBinding the Controller provisions.</summary>
    public static string DeployerClusterScopedName(string namespaceName) => $"{DeployerName}-{namespaceName}";

    /// <summary>The Secret referenced by both Job types.</summary>
    public string EffectivePullSecret => config.ImagePullSecret ?? PullSecretName;

    /// <summary>The initial and recovery launch reference; never resolved to a digest.</summary>
    public string LatestImage => $"{config.ContainerRegistrySystemHostname}/{ControllerRepository}:latest";

    public static Dictionary<string, string> Labels(string instance, string name, string? component)
    {
        var labels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = name,
            ["app.kubernetes.io/part-of"] = "codenow-data-plane",
            ["app.kubernetes.io/instance"] = instance,
            ["app.kubernetes.io/managed-by"] = "cn-cli",
        };
        if (component is not null)
            labels["app.kubernetes.io/component"] = component;

        return labels;
    }

    private Dictionary<string, string> ControllerLabels() => Labels(clusterInstanceId, ControllerName, "update-controller");

    private Dictionary<string, string> SharedLabels() => Labels(clusterInstanceId, "cn-data-plane", null);

    public V1ConfigMap Identity() => new()
    {
        ApiVersion = "v1",
        Kind = "ConfigMap",
        Metadata = new V1ObjectMeta { Name = IdentityName, NamespaceProperty = Namespace, Labels = SharedLabels() },
        Data = new Dictionary<string, string> { ["clusterInstanceId"] = clusterInstanceId },
    };

    public V1Secret Authorization() => new()
    {
        ApiVersion = "v1",
        Kind = "Secret",
        Type = "Opaque",
        Metadata = new V1ObjectMeta { Name = AuthorizationName, NamespaceProperty = Namespace, Labels = SharedLabels() },
        Data = new Dictionary<string, byte[]> { ["token"] = Encoding.UTF8.GetBytes(config.AuthorizationToken!) },
    };

    public V1Secret PullSecret()
    {
        var credentials = config.ContainerRegistrySystem!;
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        var document = new JsonObject
        {
            ["auths"] = new JsonObject
            {
                [config.ContainerRegistrySystemHostname!] = new JsonObject { ["auth"] = auth },
            },
        };

        return new V1Secret
        {
            ApiVersion = "v1",
            Kind = "Secret",
            Type = "kubernetes.io/dockerconfigjson",
            Metadata = new V1ObjectMeta { Name = PullSecretName, NamespaceProperty = Namespace, Labels = SharedLabels() },
            Data = new Dictionary<string, byte[]> { [".dockerconfigjson"] = Encoding.UTF8.GetBytes(document.ToJsonString()) },
        };
    }

    /// <summary>Controller configuration; runtime overrides already stored are preserved across reruns.</summary>
    public V1ConfigMap Configuration(string? existingJson)
    {
        var document = new JsonObject();
        if (existingJson is not null && JsonNode.Parse(existingJson) is JsonObject existing)
        {
            foreach (var key in new[] { "deploymentStartupTimeoutSeconds", "deploymentMaxAttempts", "deploymentRetryBaseDelaySeconds" })
            {
                if (existing[key] is { } value)
                    document[key] = value.DeepClone();
            }
        }

        document["controlPlaneUrl"] = config.ControlPlaneUrl;
        document["imagePullSecret"] = EffectivePullSecret;
        document["containerRegistrySystemHostname"] = config.ContainerRegistrySystemHostname;
        if (config.HttpProxy is { } proxy)
        {
            document["httpProxy"] = new JsonObject
            {
                ["hostname"] = proxy.Hostname,
                ["port"] = proxy.Port,
                ["noProxyHostnames"] = proxy.NoProxyHostnames,
            };
        }

        return new V1ConfigMap
        {
            ApiVersion = "v1",
            Kind = "ConfigMap",
            Metadata = new V1ObjectMeta { Name = ConfigName, NamespaceProperty = Namespace, Labels = ControllerLabels() },
            Data = new Dictionary<string, string> { ["config.json"] = document.ToJsonString() },
        };
    }

    public V1ServiceAccount ServiceAccount() => new()
    {
        ApiVersion = "v1",
        Kind = "ServiceAccount",
        Metadata = new V1ObjectMeta { Name = ControllerName, NamespaceProperty = Namespace, Labels = ControllerLabels() },
    };

    /// <summary>
    /// Namespaced operations, exactly the Controller access matrix. Fixed names restrict <c>get</c> and
    /// <c>patch</c>; <c>create</c> cannot use <c>resourceNames</c>, so it has its own rules. Attempt
    /// journals and their Roles/RoleBindings have dynamic names, so their rules are namespace-wide.
    /// </summary>
    public V1Role Role() => new()
    {
        ApiVersion = $"{RbacGroup}/v1",
        Kind = "Role",
        Metadata = new V1ObjectMeta { Name = ControllerName, NamespaceProperty = Namespace, Labels = ControllerLabels() },
        Rules =
        [
            Rule("batch", ["cronjobs"], ["get", "patch"], [ControllerName]),
            Rule("batch", ["jobs"], ["get", "list", "watch", "create", "delete"]),
            Rule("", ["pods"], ["list", "watch"]),
            Rule("", ["configmaps"], ["get", "list", "watch", "create", "patch", "delete"]),
            Rule("", ["persistentvolumeclaims"], ["get"], ["cn-dp-pulumi-state"]),
            Rule("", ["persistentvolumeclaims"], ["create"]),
            Rule("", ["secrets"], ["get"], [AuthorizationName, "cn-dp-pulumi-passphrase", "cn-dp-deployer-secrets"]),
            Rule("", ["secrets"], ["patch"], ["cn-dp-deployer-secrets"]),
            Rule("", ["secrets"], ["create"]),
            Rule("", ["serviceaccounts"], ["get", "patch"], [DeployerName]),
            Rule("", ["serviceaccounts"], ["create"]),
            Rule(RbacGroup, ["roles", "rolebindings"], ["get", "list", "create", "patch", "delete"]),
        ],
    };

    public V1RoleBinding RoleBinding() => new()
    {
        ApiVersion = $"{RbacGroup}/v1",
        Kind = "RoleBinding",
        Metadata = new V1ObjectMeta { Name = ControllerName, NamespaceProperty = Namespace, Labels = ControllerLabels() },
        RoleRef = new V1RoleRef { ApiGroup = RbacGroup, Kind = "Role", Name = ControllerName },
        Subjects = [Subject()],
    };

    /// <summary>
    /// Cluster-scoped operations of the Controller access matrix: Deployer RBAC provisioning, and the system
    /// namespaces with their image pull Secret (the namespaces come from configuration, so these rules are
    /// cluster-wide). Then the whole approved Deployer profile, so Kubernetes delegation checks pass without
    /// <c>escalate</c> or <c>bind</c> overrides. The profile is additive: its broad rules are not narrowed by
    /// the operational name restrictions.
    /// </summary>
    public V1ClusterRole ClusterRole()
    {
        var rules = OperationalClusterRules();
        rules.AddRange(DeployerProfile());

        return new V1ClusterRole
        {
            ApiVersion = $"{RbacGroup}/v1",
            Kind = "ClusterRole",
            Metadata = new V1ObjectMeta { Name = ClusterScopedName(Namespace), Labels = ControllerLabels() },
            Rules = rules,
        };
    }

    /// <summary>The Controller-specific cluster-scoped rules, without the Deployer profile.</summary>
    public List<V1PolicyRule> OperationalClusterRules() =>
    [
        Rule(RbacGroup, ["clusterroles", "clusterrolebindings"], ["get", "patch"], [DeployerClusterScopedName(Namespace)]),
        Rule(RbacGroup, ["clusterroles", "clusterrolebindings"], ["create"]),
        Rule("", ["namespaces"], ["get", "create", "patch"]),
        Rule("", ["secrets"], ["get", "patch"], [PullSecretName]),
        Rule("", ["secrets"], ["create"]),
    ];

    public V1ClusterRoleBinding ClusterRoleBinding() => new()
    {
        ApiVersion = $"{RbacGroup}/v1",
        Kind = "ClusterRoleBinding",
        Metadata = new V1ObjectMeta { Name = ClusterScopedName(Namespace), Labels = ControllerLabels() },
        RoleRef = new V1RoleRef { ApiGroup = RbacGroup, Kind = "ClusterRole", Name = ClusterScopedName(Namespace) },
        Subjects = [Subject()],
    };

    public V1CronJob CronJob(string image)
    {
        var labels = ControllerLabels();
        return new V1CronJob
        {
            ApiVersion = "batch/v1",
            Kind = "CronJob",
            Metadata = new V1ObjectMeta { Name = ControllerName, NamespaceProperty = Namespace, Labels = labels },
            Spec = new V1CronJobSpec
            {
                Schedule = Schedule,
                ConcurrencyPolicy = "Forbid",
                SuccessfulJobsHistoryLimit = 1,
                FailedJobsHistoryLimit = 3,
                JobTemplate = new V1JobTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = new Dictionary<string, string>(labels) },
                    Spec = new V1JobSpec
                    {
                        ActiveDeadlineSeconds = RunDeadlineSeconds,
                        BackoffLimit = 0,
                        Template = new V1PodTemplateSpec
                        {
                            Metadata = new V1ObjectMeta { Labels = new Dictionary<string, string>(labels) },
                            Spec = PodSpec(image),
                        },
                    },
                },
            },
        };
    }

    private V1PodSpec PodSpec(string image) => new()
    {
        ServiceAccountName = ControllerName,
        RestartPolicy = "Never",
        TerminationGracePeriodSeconds = TerminationGraceSeconds,
        ImagePullSecrets = [new V1LocalObjectReference { Name = EffectivePullSecret }],
        SecurityContext = new V1PodSecurityContext
        {
            RunAsNonRoot = true,
            RunAsUser = NonRootId,
            RunAsGroup = NonRootId,
            FsGroup = NonRootId,
            SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
        },
        Containers =
        [
            new V1Container
            {
                Name = "update-controller",
                Image = image,
                ImagePullPolicy = "Always",
                Env = ProxyEnvironment(),
                Resources = new V1ResourceRequirements
                {
                    Requests = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("100m"), ["memory"] = new("128Mi") },
                    Limits = new Dictionary<string, ResourceQuantity> { ["cpu"] = new("500m"), ["memory"] = new("256Mi") },
                },
                SecurityContext = new V1SecurityContext
                {
                    AllowPrivilegeEscalation = false,
                    RunAsNonRoot = true,
                    Capabilities = new V1Capabilities { Drop = ["ALL"] },
                    SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" },
                },
            },
        ],
    };

    private List<V1EnvVar>? ProxyEnvironment()
    {
        if (config.HttpProxy is not { } proxy)
            return null;

        var endpoint = proxy.Hostname!.Contains(':') ? $"http://[{proxy.Hostname}]:{proxy.Port}" : $"http://{proxy.Hostname}:{proxy.Port}";
        return
        [
            new V1EnvVar { Name = "HTTP_PROXY", Value = endpoint },
            new V1EnvVar { Name = "HTTPS_PROXY", Value = endpoint },
            new V1EnvVar { Name = "NO_PROXY", Value = proxy.NoProxyHostnames ?? string.Empty },
        ];
    }

    private Rbacv1Subject Subject() => new() { Kind = "ServiceAccount", Name = ControllerName, NamespaceProperty = Namespace };

    private static V1PolicyRule Rule(string group, List<string> resources, List<string> verbs, List<string>? names = null) => new()
    {
        ApiGroups = [group],
        Resources = resources,
        Verbs = verbs,
        ResourceNames = names,
    };

    /// <summary>The approved Deployer profile rules the Controller delegates to the Deployer.</summary>
    public static List<V1PolicyRule> DeployerProfile()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ProfileResource)
            ?? throw new InvalidOperationException("Deployer permission profile is not embedded.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("clusterRules").EnumerateArray().Select(rule => new V1PolicyRule
        {
            ApiGroups = Strings(rule, "apiGroups"),
            Resources = Strings(rule, "resources"),
            NonResourceURLs = Strings(rule, "nonResourceURLs"),
            Verbs = Strings(rule, "verbs")!,
        }).ToList();
    }

    private static List<string>? Strings(JsonElement rule, string name) =>
        rule.TryGetProperty(name, out var values) ? values.EnumerateArray().Select(value => value.GetString()!).ToList() : null;
}
