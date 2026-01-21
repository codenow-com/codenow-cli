namespace CodeNOW.Cli.Common.Console.Commands;

/// <summary>
/// Simple command result with exit code and optional error message.
/// </summary>
internal sealed record Result(bool Success, int ExitCode, string? Error = null)
{
    /// <summary>
    /// Creates a successful result with exit code 0.
    /// </summary>
    public static Result Ok() => new(true, 0);

    /// <summary>
    /// Creates a failed result with an optional message and exit code.
    /// </summary>
    public static Result Fail(string error = "", int code = 1)
        => new(false, code, error);
}
