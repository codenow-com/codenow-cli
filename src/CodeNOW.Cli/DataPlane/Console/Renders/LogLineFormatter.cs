using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CodeNOW.Cli.DataPlane.Console.Renders;

/// <summary>
/// Applies highlighting and sanitization for log lines.
/// </summary>
internal static class LogLineFormatter
{
    /// <summary>
    /// Removes ANSI sequences and control characters from a log line.
    /// </summary>
    public static string SanitizeLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        var builder = new StringBuilder(line.Length);
        var inEscape = false;

        foreach (var ch in line)
        {
            if (inEscape)
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                    inEscape = false;
                continue;
            }

            if (ch == '\u001b')
            {
                inEscape = true;
                continue;
            }

            if (ch < 32)
            {
                if (ch == '\t')
                    builder.Append(' ');
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a log line into a Spectre.Console renderable.
    /// </summary>
    public static IRenderable FormatLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return new Text(line);

        var spans = FindLogHighlightSpans(line);
        if (spans.Count == 0)
            return new Text(line);

        var builder = new StringBuilder(line.Length + spans.Count * 8);
        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start > cursor)
                builder.Append(Markup.Escape(line[cursor..span.Start]));

            var text = Markup.Escape(line.Substring(span.Start, span.Length));
            builder.Append($"[{span.Color}]{text}[/]");
            cursor = span.Start + span.Length;
        }

        if (cursor < line.Length)
            builder.Append(Markup.Escape(line[cursor..]));

        return new Markup(builder.ToString());
    }

    private static List<LogSpan> FindLogHighlightSpans(string line)
    {
        var spans = new List<LogSpan>();

        AddRegexSpans(spans, line, LogPatterns.IsoTimestamp, "grey italic");
        AddRegexSpans(spans, line, LogPatterns.Guid, "grey italic");
        AddRegexSpans(spans, line, LogPatterns.Severity, severity =>
        {
            return severity.ToUpperInvariant() switch
            {
                "TRACE" => "grey italic",
                "DEBUG" => "grey italic",
                "INFO" => "green",
                "WARN" => "yellow",
                "WARNING" => "yellow",
                "ERROR" => "red",
                "ERR" => "red",
                "FATAL" => "red",
                "PANIC" => "red",
                _ => "grey italic"
            };
        });
        AddRegexSpans(spans, line, LogPatterns.JsonKey, "cyan");
        AddRegexSpans(spans, line, LogPatterns.KeyValueKey, "cyan");
        AddRegexSpans(spans, line, LogPatterns.QuotedString, "yellow");

        spans.Sort((a, b) =>
        {
            var byStart = a.Start.CompareTo(b.Start);
            if (byStart != 0)
                return byStart;
            return b.Length.CompareTo(a.Length);
        });
        return FilterOverlaps(spans);
    }

    private static void AddRegexSpans(List<LogSpan> spans, string line, Regex regex, string color)
    {
        foreach (Match match in regex.Matches(line))
            spans.Add(new LogSpan(match.Index, match.Length, color));
    }

    private static void AddRegexSpans(List<LogSpan> spans, string line, Regex regex, Func<string, string> colorSelector)
    {
        foreach (Match match in regex.Matches(line))
        {
            var color = colorSelector(match.Value);
            spans.Add(new LogSpan(match.Index, match.Length, color));
        }
    }

    private static List<LogSpan> FilterOverlaps(List<LogSpan> spans)
    {
        var result = new List<LogSpan>();
        var lastEnd = -1;
        foreach (var span in spans)
        {
            if (span.Start >= lastEnd)
            {
                result.Add(span);
                lastEnd = span.Start + span.Length;
            }
        }
        return result;
    }

    /// <summary>
    /// Precompiled regex patterns used for log highlighting.
    /// </summary>
    private static class LogPatterns
    {
        public static readonly Regex IsoTimestamp = new(
            @"\b\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})\b",
            RegexOptions.Compiled);

        public static readonly Regex Guid = new(
            @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}\b",
            RegexOptions.Compiled);

        public static readonly Regex Severity = new(
            @"\b(TRACE|DEBUG|INFO|WARN|WARNING|ERROR|ERR|FATAL|PANIC)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static readonly Regex JsonKey = new(
            "\"(?<key>[^\"]+)\"\\s*:",
            RegexOptions.Compiled);

        public static readonly Regex KeyValueKey = new(
            @"\b[A-Za-z0-9_.-]+(?==)",
            RegexOptions.Compiled);

        public static readonly Regex QuotedString = new(
            "\"([^\"\\\\]|\\\\.)*\"",
            RegexOptions.Compiled);
    }

    /// <summary>
    /// Defines a colored range within a log line.
    /// </summary>
    private readonly record struct LogSpan(int Start, int Length, string Color);
}
