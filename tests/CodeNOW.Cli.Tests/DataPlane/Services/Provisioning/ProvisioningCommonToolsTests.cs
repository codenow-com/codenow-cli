using System.Text.Json.Nodes;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Json;
using CodeNOW.Cli.DataPlane;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Provisioning;

public class ProvisioningCommonToolsTests
{
    [Fact]
    public void BootstrapLabels_UsesExpectedValues()
    {
        var labels = ProvisioningCommonTools.BootstrapLabels;

        Assert.Equal(DataPlaneConstants.BootstrapAppLabelValue, labels["app.kubernetes.io/name"]);
        Assert.Equal(KubernetesConstants.LabelValues.ManagedBy, labels["app.kubernetes.io/managed-by"]);
        Assert.Equal(DataPlaneConstants.PartOfDataPlaneLabelValue, labels["app.kubernetes.io/part-of"]);
    }

    [Fact]
    public void EnsureProxyEnv_DoesNothingWhenDisabled()
    {
        var container = new JsonObject();
        var config = new OperatorConfig();
        config.HttpProxy.Enabled = false;
        config.HttpProxy.Hostname = "proxy.example.com";
        config.HttpProxy.Port = 3128;

        ProvisioningCommonTools.EnsureProxyEnv(container, config);

        Assert.Null(container["env"]);
    }

    [Fact]
    public void EnsureProxyEnv_AddsProxyVariablesOnce()
    {
        var container = new JsonObject();
        var config = new OperatorConfig();
        config.HttpProxy.Enabled = true;
        config.HttpProxy.Hostname = "proxy.example.com";
        config.HttpProxy.Port = 3128;
        config.HttpProxy.NoProxy = ".cluster.local";

        ProvisioningCommonTools.EnsureProxyEnv(container, config);
        ProvisioningCommonTools.EnsureProxyEnv(container, config);

        var env = container["env"]!.AsArray();
        Assert.Equal(3, env.Count);
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "HTTP_PROXY");
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "HTTPS_PROXY");
        Assert.Contains(env, node => node?["name"]?.GetValue<string>() == "NO_PROXY");
    }

    [Fact]
    public void EnsureProxyEnv_SkipsWhenHostnameOrPortMissing()
    {
        var container = new JsonObject();
        var config = new OperatorConfig();
        config.HttpProxy.Enabled = true;
        config.HttpProxy.Hostname = "proxy.example.com";

        ProvisioningCommonTools.EnsureProxyEnv(container, config);

        Assert.Null(container["env"]);
    }

    [Fact]
    public void BuildServiceAccountVolume_BuildsProjectedVolume()
    {
        var volume = ProvisioningCommonTools.BuildServiceAccountVolume();

        Assert.Equal("serviceaccount-token", volume["name"]?.GetValue<string>());

        var projected = volume["projected"]!.AsObject();
        Assert.Equal(292, projected["defaultMode"]?.GetValue<int>());

        var sources = projected["sources"]!.AsArray();
        Assert.Equal(3, sources.Count);

        var token = sources[0]!["serviceAccountToken"]!.AsObject();
        Assert.Equal(3607, token["expirationSeconds"]?.GetValue<int>());
        Assert.Equal("token", token["path"]?.GetValue<string>());

        var configMap = sources[1]!["configMap"]!.AsObject();
        Assert.Equal("kube-root-ca.crt", configMap["name"]?.GetValue<string>());
        var configMapItems = configMap["items"]!.AsArray();
        Assert.Equal("ca.crt", configMapItems[0]!["key"]?.GetValue<string>());
        Assert.Equal("ca.crt", configMapItems[0]!["path"]?.GetValue<string>());

        var downwardApi = sources[2]!["downwardAPI"]!.AsObject();
        var downwardItems = downwardApi["items"]!.AsArray();
        Assert.Equal("namespace", downwardItems[0]!["path"]?.GetValue<string>());
        var fieldRef = downwardItems[0]!["fieldRef"]!.AsObject();
        Assert.Equal("v1", fieldRef["apiVersion"]?.GetValue<string>());
        Assert.Equal("metadata.namespace", fieldRef["fieldPath"]?.GetValue<string>());
    }

    [Fact]
    public void BuildServiceAccountVolumeMount_BuildsReadOnlyMount()
    {
        var mount = ProvisioningCommonTools.BuildServiceAccountVolumeMount();

        Assert.Equal("serviceaccount-token", mount["name"]?.GetValue<string>());
        Assert.Equal("/var/run/secrets/kubernetes.io/serviceaccount", mount["mountPath"]?.GetValue<string>());
        Assert.True(mount["readOnly"]?.GetValue<bool>());
    }

    [Fact]
    public void ApplySystemNodePlacement_AddsTolerationsAndAffinityWhenEnabled()
    {
        var podSpec = new JsonObject();
        var config = new OperatorConfig();
        config.Kubernetes.PodPlacementMode = PodPlacementMode.NodeSelectorAndTaints;
        config.Kubernetes.NodeLabels.System.Key = "node-role.kubernetes.io/system";
        config.Kubernetes.NodeLabels.System.Value = "true";

        ProvisioningCommonTools.ApplySystemNodePlacement(podSpec, config);

        var tolerations = podSpec["tolerations"]!.AsArray();
        Assert.Single(tolerations);
        Assert.Equal("NoExecute", tolerations[0]!["effect"]?.GetValue<string>());
        Assert.Equal("node-role.kubernetes.io/system", tolerations[0]!["key"]?.GetValue<string>());
        Assert.Equal("Equal", tolerations[0]!["operator"]?.GetValue<string>());
        Assert.Equal("true", tolerations[0]!["value"]?.GetValue<string>());

        var affinity = podSpec["affinity"]!.AsObject();
        var nodeAffinity = affinity["nodeAffinity"]!.AsObject();
        var required = nodeAffinity["requiredDuringSchedulingIgnoredDuringExecution"]!.AsObject();
        var nodeSelectorTerms = required["nodeSelectorTerms"]!.AsArray();
        var matchExpressions = nodeSelectorTerms[0]!["matchExpressions"]!.AsArray();
        var expression = matchExpressions[0]!.AsObject();
        Assert.Equal("node-role.kubernetes.io/system", expression["key"]?.GetValue<string>());
        Assert.Equal("In", expression["operator"]?.GetValue<string>());
        var values = expression["values"]!.AsArray();
        Assert.Single(values);
        Assert.Equal("true", values[0]!.GetValue<string>());
    }

    [Fact]
    public void ApplyServiceAccountAttachments_PreservesExistingVolumesAndMounts()
    {
        var jsonObj = new JsonObject();
        jsonObj.Set("spec.template.spec.containers[0].volumeMounts[0].name", "data");
        jsonObj.Set("spec.template.spec.containers[0].volumeMounts[0].mountPath", "/data");
        jsonObj.Set("spec.template.spec.containers[0].volumeMounts[1].name", "tmp");
        jsonObj.Set("spec.template.spec.containers[0].volumeMounts[1].mountPath", "/tmp");
        jsonObj.Set("spec.template.spec.volumes[0].name", "data");
        jsonObj.Set("spec.template.spec.volumes[1].name", "tmp");

        ProvisioningCommonTools.ApplyServiceAccountAttachments(jsonObj);

        var podSpec = jsonObj["spec"]!["template"]!["spec"]!.AsObject();
        var volumeMounts = podSpec["containers"]!.AsArray()[0]!.AsObject()["volumeMounts"]!.AsArray();
        Assert.Equal(3, volumeMounts.Count);
        Assert.Equal("data", volumeMounts[0]!["name"]?.GetValue<string>());
        Assert.Equal("tmp", volumeMounts[1]!["name"]?.GetValue<string>());
        Assert.Equal("serviceaccount-token", volumeMounts[2]!["name"]?.GetValue<string>());

        var volumes = podSpec["volumes"]!.AsArray();
        Assert.Equal(3, volumes.Count);
        Assert.Equal("data", volumes[0]!["name"]?.GetValue<string>());
        Assert.Equal("tmp", volumes[1]!["name"]?.GetValue<string>());
        Assert.Equal("serviceaccount-token", volumes[2]!["name"]?.GetValue<string>());
    }
}
