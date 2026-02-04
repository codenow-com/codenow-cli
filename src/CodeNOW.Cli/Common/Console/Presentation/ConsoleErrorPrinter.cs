using Spectre.Console;

namespace CodeNOW.Cli.Common.Console.Presentation;

/// <summary>
/// Prints error messages in a standard format.
/// </summary>
public static class ConsoleErrorPrinter
{
    /// <summary>
    /// Prints an error message in a standard format.
    /// </summary>
    public static void PrintError(string message, string? details = null)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            AnsiConsole.MarkupLine($"[red]Error: {message}[/]\n");
            return;
        }

        AnsiConsole.MarkupLine($"[red]Error: {message}\n{details}[/]\n");
    }
}
