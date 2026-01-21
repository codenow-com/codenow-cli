using System.Text;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Runtimes;

/// <summary>
/// Forces Spectre.Console output to treat the console as a terminal.
/// </summary>
internal sealed class ForcedTerminalOutput : IAnsiConsoleOutput
{
    private readonly IConsoleHost _console;

    /// <summary>
    /// Creates a terminal output wrapper.
    /// </summary>
    public ForcedTerminalOutput(TextWriter writer, IConsoleHost console)
    {
        Writer = writer;
        _console = console;
    }

    /// <inheritdoc />
    public TextWriter Writer { get; }
    /// <inheritdoc />
    public bool IsTerminal => true;
    /// <inheritdoc />
    public int Width => _console.WindowWidth;
    /// <inheritdoc />
    public int Height => _console.WindowHeight;

    /// <inheritdoc />
    public void SetEncoding(Encoding encoding)
    {
        Writer.Flush();
        _console.OutputEncoding = encoding;
    }
}
