using CodeNOW.Cli.Common.Console.Commands;
using CodeNOW.Cli.DataPlane.Console.Models;
using CodeNOW.Cli.DataPlane.Console.Prompts;
using CodeNOW.Cli.DataPlane.Console.Supports;
using CodeNOW.Cli.DataPlane.Models;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Security.Cryptography;

namespace CodeNOW.Cli.DataPlane.Console.Commands;

/// <summary>
/// CLI command for bootstrapping the Data Plane operator.
/// </summary>
public class BootstrapCommand(ILogger<BootstrapCommand> logger, IBootstrapService bootstrapService)
{
    private readonly BootstrapConfigStore configStore = new();
    private readonly BootstrapWizard wizard = new();
    private readonly PulumiOperatorInfoProvider operatorInfoProvider = new(logger);

    /// <summary>
    /// Bootstraps the Kubernetes cluster for Data Plane installation.
    /// </summary>
    /// <param name="config">
    /// Path to an existing Data Plane Operator configuration file. Encryption key must be provided via CN_DP_OPERATOR_ENCRYPTION_KEY.
    /// </param>
    /// <returns>Process exit code.</returns>
    [Command("bootstrap")]
    /// <summary>
    /// Runs the bootstrap flow to provision the data plane.
    /// </summary>
    public async Task<int> Bootstrap([HideDefaultValue] string config = "")
    {
        var configSource = ResolveConfigSource(config);
        var result = await RunBootstrapFlowAsync(configSource, config);
        if (!result.Success)
        {
            return result.ExitCode;
        }
        return 0;
    }

    /// <summary>
    /// Determines the configuration source based on the provided path or user prompt.
    /// </summary>
    /// <param name="configPath">Optional path to an existing configuration file.</param>
    /// <returns>The selected configuration source.</returns>
    private ConfigSource ResolveConfigSource(string configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
            return ConfigSource.Existing;

        return AnsiConsole.Prompt(
        new SelectionPrompt<ConfigSource>()
            .Title("Select a [green]Data Plane Operator[/] configuration source:")
            .AddChoices(
                ConfigSource.Generate,
                ConfigSource.Existing
            )
            .UseConverter(choice => choice switch
            {
                ConfigSource.Generate =>
                    "Generate " + DataPlaneConstants.OperatorConfigFileName + " using the setup wizard",

                ConfigSource.Existing =>
                    "Use an existing " + DataPlaneConstants.OperatorConfigFileName,

                _ => choice.ToString()
            })
        );
    }

    /// <summary>
    /// Executes the bootstrap flow for the selected configuration source.
    /// </summary>
    /// <param name="source">Source of the operator configuration.</param>
    /// <param name="configPath">Optional path to an existing configuration file.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> RunBootstrapFlowAsync(ConfigSource source, string configPath)
    {
        return source switch
        {
            ConfigSource.Generate => await RunSetupWizardAsync(),
            ConfigSource.Existing => await UseExistingConfigAsync(configPath),
            _ => Result.Fail("Invalid configuration source.")
        };
    }

    /// <summary>
    /// Runs the interactive setup wizard to generate or edit a configuration file.
    /// </summary>
    /// <param name="existingConfig">Existing configuration used for prefilled edit mode.</param>
    /// <param name="defaultOutputPath">Default output path to suggest when saving.</param>
    /// <param name="encryptionKey">Optional existing encryption key to reuse.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> RunSetupWizardAsync(
        OperatorConfig? existingConfig = null,
        string? defaultOutputPath = null,
        string? encryptionKey = null)
    {
        var (result, opConfig) = wizard.Run(
            existingConfig,
            defaultOutputPath,
            encryptionKey,
            DataPlaneConstants.OperatorConfigFileName,
            configStore,
            operatorInfoProvider);
        if (!result.Success || opConfig is null)
            return result;

        await BootstrapWithStatusAsync(opConfig);
        return Result.Ok();
    }

    /// <summary>
    /// Loads and validates an existing configuration file using an encryption key,
    /// optionally opening the edit wizard with prefilled values.
    /// </summary>
    /// <param name="configPath">Path to the existing configuration file or empty to prompt.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> UseExistingConfigAsync(string? configPath)
    {
        const int MaxEncryptionKeyAttempts = 3;
        var resolveResult = ResolveConfigPath(configPath, out var filePath, out var fromArgs);
        if (resolveResult is not null)
            return resolveResult;

        if (fromArgs)
        {
            var (keyResult, encryptionKey) = GetEncryptionKeyFromEnv();
            if (keyResult is not null || encryptionKey is null)
                return keyResult ?? Result.Fail();

            try
            {
                var opConfig = configStore.LoadConfig(filePath, encryptionKey);
                operatorInfoProvider.EnsurePulumiPluginsVersion(opConfig);
                logger.LogInformation("Loaded configuration from {FilePath}.", filePath);
                await BootstrapWithStatusAsync(opConfig);
                return Result.Ok();
            }
            catch (CryptographicException)
            {
                PrintInvalidEncryptionKeyMessage();
                return Result.Fail();
            }
        }

        return await TryLoadConfigWithRetries(filePath, MaxEncryptionKeyAttempts);
    }

    /// <summary>
    /// Executes the bootstrap service under a status spinner.
    /// </summary>
    /// <param name="opConfig">Operator configuration to bootstrap.</param>
    private async Task BootstrapWithStatusAsync(OperatorConfig opConfig)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync("Bootstrapping Data Plane...", async ctx =>
            {
                ctx.Status("Bootstrapping Data Plane resources...");
                await bootstrapService.BootstrapAsync(opConfig);
            });

        AnsiConsole.MarkupLine("\n[grey]:check_mark_button: Data Plane bootstrap completed successfully.[/]\n");
    }

    /// <summary>
    /// Prints a standard error message for invalid decryption attempts.
    /// </summary>
    private static void PrintInvalidEncryptionKeyMessage()
    {
        PrintError("Invalid encryption key or corrupted configuration file.");
        AnsiConsole.MarkupLine("[grey]Please try again.[/]\n");
    }

    private static Result? ResolveConfigPath(string? configPath, out string filePath, out bool fromArgs)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            fromArgs = true;
            if (!File.Exists(configPath))
            {
                PrintError(
                    "Configuration file not found.",
                    $"Provided path: {Markup.Escape(Path.GetFullPath(configPath))}");
                filePath = "";
                return Result.Fail();
            }
            filePath = Path.GetFullPath(configPath);
            return null;
        }

        fromArgs = false;
        AnsiConsole.MarkupLine(
            "[grey]Enter a path to an existing Data Plane Operator configuration file.[/]\n");
        while (true)
        {
            filePath = AnsiConsole.Prompt(
                new TextPrompt<string>(
                    $"Configuration file path [green]({DataPlaneConstants.OperatorConfigFileName})[/]      :")
                    .DefaultValue(DataPlaneConstants.OperatorConfigFileName)
                    .ShowDefaultValue(false)
                    .Validate(path =>
                    {
                        if (string.IsNullOrWhiteSpace(path))
                            return ValidationResult.Error("[red]Error: Path cannot be empty.[/]\n");
                        return ValidationResult.Success();
                    })
            );

            if (!File.Exists(filePath))
            {
                PrintError(
                    "Configuration file not found.",
                    $"Provided path: {Markup.Escape(Path.GetFullPath(filePath))}");
                continue;
            }

            filePath = Path.GetFullPath(filePath);
            return null;
        }
    }

    private static (Result? Result, string? EncryptionKey) GetEncryptionKeyFromEnv()
    {
        var encryptionKey = Environment.GetEnvironmentVariable(EnvironmentVariables.OperatorEncryptionKey);
        if (!string.IsNullOrWhiteSpace(encryptionKey))
            return (null, encryptionKey);

        PrintError("Encryption key is required when using --config.");
        AnsiConsole.MarkupLine(
            "[grey]Set the key using the " + EnvironmentVariables.OperatorEncryptionKey + " environment variable.[/]\n");
        AnsiConsole.MarkupLine(
            "[grey]Example:[/]");
        AnsiConsole.MarkupLine(
            "[grey]  export " + EnvironmentVariables.OperatorEncryptionKey + "=\"your-secret-key\"[/]");
        AnsiConsole.MarkupLine(
            "[grey]  dp bootstrap --config " + DataPlaneConstants.OperatorConfigFileName + "[/]\n");
        return (Result.Fail(), null);
    }

    private async Task<Result> TryLoadConfigWithRetries(string filePath, int maxAttempts)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
            try
            {
                string encryptionKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("Encryption key for this configuration                      :").Secret());
                var opConfig = configStore.LoadConfig(filePath, encryptionKey);
                operatorInfoProvider.EnsurePulumiPluginsVersion(opConfig);
                logger.LogInformation("Loaded configuration from {FilePath}.", filePath);
                var editExisting = AnsiConsole.Prompt(
                    new TextPrompt<bool>("Edit existing configuration? [blue][[y/n]][/] [green](n)[/]                     :")
                        .AddChoice(true)
                        .AddChoice(false)
                        .DefaultValue(false)
                        .ShowChoices(false)
                        .ShowDefaultValue(false)
                        .WithConverter(choice => choice ? "y" : "n"));
                if (editExisting)
                {
                    AnsiConsole.WriteLine();
                    return await RunSetupWizardAsync(opConfig, filePath, encryptionKey);
                }
                await BootstrapWithStatusAsync(opConfig);
                return Result.Ok();
            }
            catch (CryptographicException)
            {
                if (attempt == maxAttempts)
                {
                    PrintError("Too many failed attempts. Unable to decrypt the configuration file.");
                    return Result.Fail();
                }
                PrintInvalidEncryptionKeyMessage();
            }

        return Result.Fail();
    }

    private static void PrintError(string message, string? details = null)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            AnsiConsole.MarkupLine($"[red]Error: {message}[/]\n");
            return;
        }

        AnsiConsole.MarkupLine($"[red]Error: {message}\n{details}[/]\n");
    }

}
