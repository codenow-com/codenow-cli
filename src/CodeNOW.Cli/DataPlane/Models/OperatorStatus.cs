namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Captures the resolved status of the data plane operator.
/// </summary>
/// <param name="Namespace">Namespace where the operator pod is running.</param>
/// <param name="Version">Operator version label.</param>
/// <param name="Status">Rendered pod status text.</param>
public sealed record OperatorStatus(string Namespace, string Version, string Status)
{
    /// <summary>
    /// Represents a missing operator pod.
    /// </summary>
    public static readonly OperatorStatus NotFound = new("Unknown", "Unknown", "Unknown");

    /// <summary>
    /// Represents a failed status read.
    /// </summary>
    public static readonly OperatorStatus Error = new("Error", "Error", "Error");
}
