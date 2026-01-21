namespace CodeNOW.Cli.DataPlane.Console.Models;

/// <summary>
/// Result of handling a dashboard key input.
/// </summary>
internal sealed record DashboardKeyResult(
    LogView View,
    bool Exit,
    bool NeedsRender,
    bool ForceTopRefresh,
    bool ForceLogsRefresh,
    DateTime? MessageUntil)
{
    /// <summary>
    /// Returns a result indicating no action was taken.
    /// </summary>
    public static DashboardKeyResult Noop(LogView view)
        => new(view, false, false, false, false, null);

    /// <summary>
    /// Returns a result indicating the dashboard should exit.
    /// </summary>
    public static DashboardKeyResult Quit(LogView view)
        => new(view, true, false, false, false, null);
}
