namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Represents the outcome of a log read attempt.
/// </summary>
/// <param name="IsAvailable">Whether log content is available.</param>
/// <param name="Logs">Log content when available.</param>
/// <param name="PodName">Pod name used for the log read.</param>
/// <param name="PodUid">Pod UID used for the log read.</param>
public sealed record LogReadResult(bool IsAvailable, string Logs, string? PodName, string? PodUid)
{
    /// <summary>
    /// Shared instance for unavailable logs.
    /// </summary>
    public static LogReadResult Unavailable { get; } = new(false, string.Empty, null, null);

    /// <summary>
    /// Creates an available log result from raw log text.
    /// </summary>
    public static LogReadResult FromLogs(string logs, string? podName, string? podUid) =>
        new(true, logs ?? string.Empty, podName, podUid);

    /// <summary>
    /// Creates an unavailable log result while preserving pod identity data.
    /// </summary>
    public static LogReadResult UnavailableWithPod(string? podName, string? podUid) =>
        new(false, string.Empty, podName, podUid);
}
