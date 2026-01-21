using CodeNOW.Cli.DataPlane.Console.Models;
using CodeNOW.Cli.DataPlane.Console.Renders;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Operations;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Logs;

/// <summary>
/// Fetches and pages logs for the dashboard views.
/// </summary>
internal sealed class LogTailer
{
    private const string UnavailableMessage = "Logs are currently unavailable.";
    private readonly IManagementService _service;
    private readonly IAnsiConsole _console;
    private readonly int _topHeight;
    private readonly int _logsChromeHeight;

    /// <summary>
    /// Creates a log tailer for the dashboard.
    /// </summary>
    public LogTailer(IManagementService service, IAnsiConsole console, int topHeight, int logsChromeHeight)
    {
        _service = service;
        _console = console;
        _topHeight = topHeight;
        _logsChromeHeight = logsChromeHeight;
    }

    /// <summary>
    /// Fetches logs and updates the dashboard state.
    /// </summary>
    public async Task UpdateLogsAsync(DashboardState state, LogView view, int tailLines)
    {
        var query = new ManagementLogQuery(state.Operator.Namespace, tailLines);
        var logResult = view == LogView.Workspace
            ? await _service.GetWorkspaceLogsAsync(query)
            : await _service.GetOperatorLogsAsync(query);
        var logsToDisplay = ResolveLogsForDisplay(state, view, logResult);
        ApplyLogsText(state, logsToDisplay);
    }

    /// <summary>
    /// Scrolls the log view up by one page.
    /// </summary>
    public bool ScrollUp(DashboardState state)
    {
        state.LogOffset = ClampLogOffset(state.LogOffset - state.LogPageSize, state.LogTotal, state.LogPageSize);
        state.LogFollowTail = false;
        UpdateLogPage(state);
        return true;
    }

    /// <summary>
    /// Scrolls the log view down by one page.
    /// </summary>
    public bool ScrollDown(DashboardState state)
    {
        state.LogOffset = ClampLogOffset(state.LogOffset + state.LogPageSize, state.LogTotal, state.LogPageSize);
        state.LogFollowTail = state.LogOffset >= GetTailOffset(state.LogTotal, state.LogPageSize);
        UpdateLogPage(state);
        return true;
    }

    /// <summary>
    /// Resets the log cursor to the tail.
    /// </summary>
    public void ResetLogCursor(DashboardState state)
    {
        state.LogOffset = 0;
        state.LogFollowTail = true;
    }

    /// <summary>
    /// Requests a fresh log fetch for the current view.
    /// </summary>
    public bool RequestFreshLogs(DashboardState state, LogView view)
    {
        if (!HasPendingLogs(state))
            return false;

        SetForceFresh(state, view);
        ClearPendingLogs(state, view);
        return true;
    }

    /// <summary>
    /// Applies a fallback message when logs cannot be loaded.
    /// </summary>
    public void ApplyUnavailableMessage(DashboardState state)
    {
        ApplyLogsText(state, UnavailableMessage);
    }

    private static string ResolveLogsForDisplay(
        DashboardState state,
        LogView view,
        LogReadResult logResult)
    {
        var currentLogs = GetCurrentLogsText(state, view);
        var podUid = logResult.PodUid;
        if (IsForceFresh(state, view))
        {
            if (!logResult.IsAvailable || string.IsNullOrWhiteSpace(podUid))
                return UnavailableMessage;

            ClearForceFresh(state, view);
            ClearPendingLogs(state, view);
            SetCurrentPod(state, view, logResult);
            return logResult.Logs;
        }

        var currentPodUid = GetCurrentPodUid(state, view);
        if (string.IsNullOrWhiteSpace(currentPodUid) && !string.IsNullOrWhiteSpace(podUid))
        {
            SetCurrentPod(state, view, logResult);
            return logResult.IsAvailable ? logResult.Logs : UnavailableMessage;
        }

        if (!string.IsNullOrWhiteSpace(podUid) &&
            !string.IsNullOrWhiteSpace(currentPodUid) &&
            !string.Equals(currentPodUid, podUid, StringComparison.Ordinal))
        {
            SetPendingPod(state, view, logResult);
            state.StatusMessage = "Pod restarted. Press N to view new logs.";
            state.StatusSticky = true;
            return string.IsNullOrWhiteSpace(currentLogs) ? UnavailableMessage : currentLogs;
        }

        if (!logResult.IsAvailable || string.IsNullOrWhiteSpace(podUid))
            return string.IsNullOrWhiteSpace(currentLogs) ? UnavailableMessage : currentLogs;

        SetCurrentPod(state, view, logResult);
        return logResult.Logs;
    }

    private void ApplyLogsText(DashboardState state, string logs)
    {
        var wasAtTail = state.LogOffset >= GetTailOffset(state.LogTotal, state.LogPageSize);
        var bottomHeight = Math.Max(3, _console.Profile.Height - _topHeight - 1);
        var pageSize = Math.Max(1, bottomHeight - _logsChromeHeight);
        state.LogAllLines = PrepareLogLines(logs, Math.Max(1, _console.Profile.Width - 1));
        state.LogTotal = state.LogAllLines.Length;
        state.LogPageSize = pageSize;
        if (state.LogFollowTail || wasAtTail)
        {
            state.LogFollowTail = true;
            state.LogOffset = GetTailOffset(state.LogTotal, state.LogPageSize);
        }
        else
            state.LogOffset = ClampLogOffset(state.LogOffset, state.LogTotal, state.LogPageSize);
        UpdateLogPage(state);
    }

    private static string GetCurrentLogsText(DashboardState state, LogView view) =>
        view == LogView.Operator ? state.OperatorLogsText : state.WorkspaceLogsText;

    private static string? GetCurrentPodUid(DashboardState state, LogView view) =>
        view == LogView.Operator ? state.OperatorPodUid : state.WorkspacePodUid;

    private static void SetCurrentPod(DashboardState state, LogView view, LogReadResult logResult)
    {
        if (view == LogView.Operator)
        {
            state.OperatorPodName = logResult.PodName;
            state.OperatorPodUid = logResult.PodUid;
            state.OperatorLogsText = logResult.Logs ?? string.Empty;
        }
        else
        {
            state.WorkspacePodName = logResult.PodName;
            state.WorkspacePodUid = logResult.PodUid;
            state.WorkspaceLogsText = logResult.Logs ?? string.Empty;
        }
    }

    private static void SetPendingPod(DashboardState state, LogView view, LogReadResult logResult)
    {
        if (view == LogView.Operator)
        {
            state.PendingOperatorPodName = logResult.PodName;
            state.PendingOperatorPodUid = logResult.PodUid;
            state.PendingOperatorLogsText = logResult.Logs ?? string.Empty;
        }
        else
        {
            state.PendingWorkspacePodName = logResult.PodName;
            state.PendingWorkspacePodUid = logResult.PodUid;
            state.PendingWorkspaceLogsText = logResult.Logs ?? string.Empty;
        }
    }

    private static bool HasPendingLogs(DashboardState state)
    {
        return !string.IsNullOrWhiteSpace(state.PendingOperatorPodUid)
            || !string.IsNullOrWhiteSpace(state.PendingWorkspacePodUid);
    }

    private static bool IsForceFresh(DashboardState state, LogView view) =>
        view == LogView.Operator ? state.OperatorForceFreshLogs : state.WorkspaceForceFreshLogs;

    private static void SetForceFresh(DashboardState state, LogView view)
    {
        if (view == LogView.Operator)
            state.OperatorForceFreshLogs = true;
        else
            state.WorkspaceForceFreshLogs = true;
    }

    private static void ClearForceFresh(DashboardState state, LogView view)
    {
        if (view == LogView.Operator)
            state.OperatorForceFreshLogs = false;
        else
            state.WorkspaceForceFreshLogs = false;
    }

    private static void ClearPendingLogs(DashboardState state, LogView view)
    {
        if (view == LogView.Operator)
        {
            state.PendingOperatorPodName = null;
            state.PendingOperatorPodUid = null;
            state.PendingOperatorLogsText = string.Empty;
        }
        else
        {
            state.PendingWorkspacePodName = null;
            state.PendingWorkspacePodUid = null;
            state.PendingWorkspaceLogsText = string.Empty;
        }
    }

    private static void UpdateLogPage(DashboardState state)
    {
        state.LogLines = PaginateLogLines(state.LogAllLines, state.LogOffset, state.LogPageSize);
    }

    private static string[] PrepareLogLines(string logs, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(logs))
            return Array.Empty<string>();

        var rawLines = logs.Replace("\r", "").Split('\n', StringSplitOptions.None);
        var wrappedLines = new List<List<string>>(rawLines.Length);

        foreach (var rawLine in rawLines)
        {
            var sanitized = LogLineFormatter.SanitizeLine(rawLine);
            wrappedLines.Add(WrapLine(sanitized, maxWidth));
        }

        var lines = wrappedLines.SelectMany(segment => segment)
            .Select(EnsureVisibleLine)
            .ToArray();

        while (lines.Length > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines = lines[1..];

        return lines;
    }

    private static string[] PaginateLogLines(string[] lines, int offset, int pageSize)
    {
        if (lines.Length == 0)
            return Array.Empty<string>();

        offset = ClampLogOffset(offset, lines.Length, pageSize);
        return lines.Skip(offset).Take(pageSize).ToArray();
    }

    private static int ClampLogOffset(int offset, int total, int pageSize)
    {
        if (total <= pageSize)
            return 0;
        return Math.Clamp(offset, 0, total - pageSize);
    }

    private static List<string> WrapLine(string line, int maxWidth)
    {
        var segments = new List<string>();
        if (maxWidth <= 0)
            return segments;

        if (string.IsNullOrEmpty(line))
        {
            segments.Add(string.Empty);
            return segments;
        }

        var remaining = line;
        while (remaining.Length > maxWidth)
        {
            segments.Add(remaining[..maxWidth]);
            remaining = remaining[maxWidth..];
        }

        segments.Add(remaining);
        return segments;
    }

    private static string EnsureVisibleLine(string? line)
    {
        return string.IsNullOrEmpty(line) ? " " : line;
    }

    private static int GetTailOffset(int total, int pageSize)
    {
        if (total <= pageSize)
            return 0;
        return Math.Max(0, total - pageSize);
    }
}
