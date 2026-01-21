using System.Text;

namespace CodeNOW.Cli.DataPlane.Console.Runtimes;

/// <summary>
/// Abstraction over console I/O for testability.
/// </summary>
internal interface IConsoleHost
{
    /// <summary>
    /// Opens the standard output stream.
    /// </summary>
    Stream OpenStandardOutput();
    /// <summary>
    /// Gets whether a key press is available.
    /// </summary>
    bool KeyAvailable { get; }
    /// <summary>
    /// Reads a key press from the console.
    /// </summary>
    ConsoleKeyInfo ReadKey(bool intercept);
    /// <summary>
    /// Gets the current console window width.
    /// </summary>
    int WindowWidth { get; }
    /// <summary>
    /// Gets the current console window height.
    /// </summary>
    int WindowHeight { get; }
    /// <summary>
    /// Gets or sets the console output encoding.
    /// </summary>
    Encoding OutputEncoding { get; set; }
}
