using CodeNOW.Cli.Common.Console.Commands;
using CodeNOW.Cli.Common.Console.Presentation;
using CodeNOW.Cli.DataPlane.Services.Operations;
using ConsoleAppFramework;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Commands;

/// <summary>
/// CLI command for managing the data plane operator configuration.
/// </summary>
public class ConfigCommand(OperatorConfigService operatorConfigService)
{
    /// <summary>
    /// Encrypts secret values in a plaintext operator configuration file.
    /// </summary>
    /// <param name="config">Path to the plaintext operator configuration file.</param>
    /// <returns>Process exit code.</returns>
    [Command("config encrypt")]
    public int Encrypt(string config)
    {
        if (!File.Exists(config))
        {
            ConsoleErrorPrinter.PrintError(
                "Configuration file not found.",
                $"Provided path: {Markup.Escape(Path.GetFullPath(config))}");
            return 1;

        }

        try
        {
            var encryptionKey = operatorConfigService.EncryptConfigFile(config);
            System.Console.WriteLine(encryptionKey);
            return 0;
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
