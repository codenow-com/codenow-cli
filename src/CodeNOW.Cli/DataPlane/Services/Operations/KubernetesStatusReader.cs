using CodeNOW.Cli.DataPlane.Models;
using k8s.Models;
using CodeNOW.Cli.Adapters.Kubernetes;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Reads cluster-wide Kubernetes status information.
/// </summary>
internal sealed class KubernetesStatusReader
{
    private readonly IKubernetesClient client;
    private readonly KubernetesReadExecutor executor;

    /// <summary>
    /// Creates a reader for cluster-level status data.
    /// </summary>
    public KubernetesStatusReader(IKubernetesClient client, KubernetesReadExecutor executor)
    {
        this.client = client;
        this.executor = executor;
    }

    /// <summary>
    /// Returns the Kubernetes server version string.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Server version string.</returns>
    public Task<string> GetClusterVersionAsync(CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(async () =>
        {
            var info = await client.Version.GetCodeAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(info.GitVersion))
                return info.GitVersion;

            var major = string.IsNullOrWhiteSpace(info.Major) ? "?" : info.Major;
            var minor = string.IsNullOrWhiteSpace(info.Minor) ? "?" : info.Minor;
            return $"v{major}.{minor}";
        }, "Failed to load Kubernetes version.", "Unknown");
    }

    /// <summary>
    /// Returns aggregated cluster CPU and memory capacity and requests.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Aggregated cluster resource values.</returns>
    public Task<ClusterResources> GetClusterResourcesAsync(CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(async () =>
        {
            var nodes = await client.CoreV1.ListNodeAsync(cancellationToken: cancellationToken);
            var pods = await client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: cancellationToken);

            long cpuCapacity = 0;
            long memoryCapacity = 0;
            foreach (var node in nodes.Items)
            {
                var allocatable = node.Status?.Allocatable;
                if (allocatable is null)
                    continue;

                if (allocatable.TryGetValue("cpu", out var cpu))
                    cpuCapacity += ParseCpuToMilli(cpu.ToString());
                if (allocatable.TryGetValue("memory", out var memory))
                    memoryCapacity += ParseMemoryToBytes(memory.ToString());
            }

            long cpuRequested = 0;
            long memoryRequested = 0;
            foreach (var pod in pods.Items)
            {
                var spec = pod.Spec;
                if (spec is null || string.IsNullOrWhiteSpace(spec.NodeName))
                    continue;

                var phase = pod.Status?.Phase;
                if (phase is "Succeeded" or "Failed")
                    continue;

                var (cpu, memory) = CalculatePodRequests(spec);
                cpuRequested += cpu;
                memoryRequested += memory;
            }

            return new ClusterResources(cpuRequested, cpuCapacity, memoryRequested, memoryCapacity);
        }, "Failed to load cluster resources.", ClusterResources.Empty);
    }

    private static (long cpuMilli, long memoryBytes) CalculatePodRequests(V1PodSpec spec)
    {
        long cpuSum = 0;
        long memorySum = 0;
        foreach (var container in spec.Containers ?? [])
        {
            var requests = container.Resources?.Requests;
            cpuSum += GetCpuRequest(requests);
            memorySum += GetMemoryRequest(requests);
        }

        long initCpuMax = 0;
        long initMemoryMax = 0;
        foreach (var container in spec.InitContainers ?? [])
        {
            var requests = container.Resources?.Requests;
            initCpuMax = Math.Max(initCpuMax, GetCpuRequest(requests));
            initMemoryMax = Math.Max(initMemoryMax, GetMemoryRequest(requests));
        }

        return (Math.Max(cpuSum, initCpuMax), Math.Max(memorySum, initMemoryMax));
    }

    private static long GetCpuRequest(IDictionary<string, ResourceQuantity>? requests)
    {
        if (requests is null || !requests.TryGetValue("cpu", out var quantity))
            return 0;

        return ParseCpuToMilli(quantity.ToString());
    }

    private static long GetMemoryRequest(IDictionary<string, ResourceQuantity>? requests)
    {
        if (requests is null || !requests.TryGetValue("memory", out var quantity))
            return 0;

        return ParseMemoryToBytes(quantity.ToString());
    }

    private static long ParseCpuToMilli(string? quantity)
    {
        if (string.IsNullOrWhiteSpace(quantity))
            return 0;

        quantity = quantity.Trim();
        if (quantity.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            var value = quantity[..^1];
            return long.TryParse(value, out var milli) ? milli : 0;
        }

        if (double.TryParse(quantity, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cores))
            return (long)Math.Round(cores * 1000d);

        return 0;
    }

    private static long ParseMemoryToBytes(string? quantity)
    {
        if (string.IsNullOrWhiteSpace(quantity))
            return 0;

        quantity = quantity.Trim();
        var suffix = ExtractSuffix(quantity, out var numberPart);
        if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return 0;

        return suffix switch
        {
            "Ki" => (long)Math.Round(value * 1024d),
            "Mi" => (long)Math.Round(value * 1024d * 1024d),
            "Gi" => (long)Math.Round(value * 1024d * 1024d * 1024d),
            "Ti" => (long)Math.Round(value * 1024d * 1024d * 1024d * 1024d),
            "Pi" => (long)Math.Round(value * 1024d * 1024d * 1024d * 1024d * 1024d),
            "Ei" => (long)Math.Round(value * 1024d * 1024d * 1024d * 1024d * 1024d * 1024d),
            "K" => (long)Math.Round(value * 1000d),
            "M" => (long)Math.Round(value * 1000d * 1000d),
            "G" => (long)Math.Round(value * 1000d * 1000d * 1000d),
            "T" => (long)Math.Round(value * 1000d * 1000d * 1000d * 1000d),
            "P" => (long)Math.Round(value * 1000d * 1000d * 1000d * 1000d * 1000d),
            "E" => (long)Math.Round(value * 1000d * 1000d * 1000d * 1000d * 1000d * 1000d),
            _ => (long)Math.Round(value)
        };
    }

    private static string ExtractSuffix(string quantity, out string numberPart)
    {
        if (quantity.Length >= 2 && char.IsLetter(quantity[^1]) && char.IsLetter(quantity[^2]))
        {
            numberPart = quantity[..^2];
            return quantity[^2..];
        }

        if (quantity.Length >= 1 && char.IsLetter(quantity[^1]))
        {
            numberPart = quantity[..^1];
            return quantity[^1..];
        }

        numberPart = quantity;
        return string.Empty;
    }
}
