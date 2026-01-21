namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Aggregated cluster capacity and requested resources.
/// </summary>
/// <param name="CpuRequestedMilli">Requested CPU in millicores.</param>
/// <param name="CpuCapacityMilli">Allocatable CPU in millicores.</param>
/// <param name="MemoryRequestedBytes">Requested memory in bytes.</param>
/// <param name="MemoryCapacityBytes">Allocatable memory in bytes.</param>
public sealed record ClusterResources(
    long CpuRequestedMilli,
    long CpuCapacityMilli,
    long MemoryRequestedBytes,
    long MemoryCapacityBytes)
{
    /// <summary>
    /// Empty resource snapshot.
    /// </summary>
    public static readonly ClusterResources Empty = new(0, 0, 0, 0);
}
