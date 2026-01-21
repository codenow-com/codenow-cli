using System.Text;
using SystemConsole = System.Console;

namespace CodeNOW.Cli.DataPlane.Console.Runtimes;

/// <summary>
/// Wraps <see cref="System.Console"/> for testable console access.
/// </summary>
internal sealed class SystemConsoleHost : IConsoleHost
{
    /// <inheritdoc />
    public Stream OpenStandardOutput() => SystemConsole.OpenStandardOutput();

    /// <inheritdoc />
    public bool KeyAvailable => SystemConsole.KeyAvailable;

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept) => SystemConsole.ReadKey(intercept);

    /// <inheritdoc />
    public int WindowWidth => SystemConsole.WindowWidth;

    /// <inheritdoc />
    public int WindowHeight => SystemConsole.WindowHeight;

    /// <inheritdoc />
    public Encoding OutputEncoding
    {
        get => SystemConsole.OutputEncoding;
        set => SystemConsole.OutputEncoding = value;
    }
}
