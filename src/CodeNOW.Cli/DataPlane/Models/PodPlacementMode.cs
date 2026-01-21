using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Pod placement strategy used by the operator.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PodPlacementMode>))]
public enum PodPlacementMode
{
    /// <summary>
    /// Place pods using node selector only.
    /// </summary>
    PodNodeSelector,
    /// <summary>
    /// Place pods using node selector and taints/tolerations.
    /// </summary>
    NodeSelectorAndTaints
}

public static class PodPlacementModeExtensions
{
    /// <summary>
    /// Converts the placement mode to the configuration string value.
    /// </summary>
    public static string ToConfigString(this PodPlacementMode mode)
    {
        return mode switch
        {
            PodPlacementMode.PodNodeSelector => "pod-node-selector",
            PodPlacementMode.NodeSelectorAndTaints => "node-selector-and-taints",
            _ => mode.ToString()
        };
    }
}
