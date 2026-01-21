using k8s.Models;

namespace CodeNOW.Cli.DataPlane.Services.Operations;

/// <summary>
/// Formats pod status into kubectl-style status text.
/// </summary>
internal static class PodStatusTextBuilder
{
    /// <summary>
    /// Builds a kubectl-style status string for a pod.
    /// </summary>
    public static string BuildPodStatusText(V1Pod pod)
    {
        if (pod.Metadata?.DeletionTimestamp is not null)
            return "Terminating";

        var initStatus = GetInitStatusText(pod);
        if (!string.IsNullOrWhiteSpace(initStatus))
            return initStatus;

        if (pod.Status?.ContainerStatuses is not null)
        {
            foreach (var status in pod.Status.ContainerStatuses)
            {
                var containerReason = GetStatusReason(status);
                if (!string.IsNullOrWhiteSpace(containerReason))
                    return containerReason;
            }
        }

        var phase = pod.Status?.Phase ?? "Unknown";
        if (phase.Equals("Succeeded", StringComparison.OrdinalIgnoreCase))
            return "Completed";
        var podReason = pod.Status?.Reason;
        return podReason is null ? phase : $"{phase} ({podReason})";
    }

    private static string? GetInitStatusText(V1Pod pod)
    {
        var statuses = pod.Status?.InitContainerStatuses;
        if (statuses is null || statuses.Count == 0)
            return null;

        var total = pod.Spec?.InitContainers?.Count ?? statuses.Count;
        var completed = 0;
        string? pendingReason = null;
        foreach (var status in statuses)
        {
            var waitingReason = status.State?.Waiting?.Reason;
            var terminatedReason = status.State?.Terminated?.Reason;
            var terminatedExitCode = status.State?.Terminated?.ExitCode;
            if (terminatedExitCode.HasValue)
            {
                if (terminatedExitCode.Value == 0)
                {
                    completed++;
                }
                else
                {
                    var reason = FilterUnknown(terminatedReason) ?? $"ExitCode:{terminatedExitCode.Value}";
                    return $"Init:{reason}";
                }
            }
            else if (!string.IsNullOrWhiteSpace(waitingReason) &&
                !waitingReason.Equals("PodInitializing", StringComparison.OrdinalIgnoreCase))
            {
                pendingReason ??= waitingReason;
            }
        }

        if (completed < total)
        {
            if (!string.IsNullOrWhiteSpace(pendingReason))
                return $"Init:{pendingReason}";
            return $"Init:{completed}/{total}";
        }

        return null;
    }

    private static string? GetStatusReason(V1ContainerStatus status)
    {
        return FilterUnknown(status.State?.Waiting?.Reason)
            ?? FilterUnknown(status.State?.Terminated?.Reason)
            ?? FilterUnknown(status.LastState?.Terminated?.Reason)
            ?? FilterUnknown(status.State?.Waiting?.Message)
            ?? FilterUnknown(status.State?.Terminated?.Message)
            ?? FilterUnknown(status.LastState?.Terminated?.Message);
    }

    private static string? FilterUnknown(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : value;
    }
}
