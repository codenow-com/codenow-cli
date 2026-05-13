using CodeNOW.Cli.Common.Console.Commands;
using CodeNOW.Cli.Common.Console.Presentation;
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
public class BootstrapCommand(
    ILogger<BootstrapCommand> logger,
    IBootstrapService bootstrapService,
    IPulumiOperatorInfoProvider operatorInfoProvider,
    IFluxCDInfoProvider fluxcdInfoProvider,
    KubernetesConnectionGuard connectionGuard)
{
    private readonly BootstrapConfigStore configStore = new();
    private readonly BootstrapWizard wizard = new();
    private readonly IPulumiOperatorInfoProvider operatorInfoProvider = operatorInfoProvider;
    private readonly IFluxCDInfoProvider fluxcdInfoProvider = fluxcdInfoProvider;
    private readonly KubernetesConnectionGuard connectionGuard = connectionGuard;

    /// <summary>
    /// Bootstraps the Kubernetes cluster for Data Plane installation.
    /// </summary>
    /// <param name="config">
    /// Path to an existing Data Plane Operator configuration file. Encryption key must be provided via CN_DP_OPERATOR_ENCRYPTION_KEY.
    /// </param>
    /// <param name="fluxcdEnable">Enable installation of FluxCD components. Applies only when generating a new config.</param>
    /// <param name="fluxcdSkipCrds">Skip FluxCD CRD installation when FluxCD is enabled. Applies only when generating a new config.</param>
    /// <param name="pulumiSkipCrds">Skip Pulumi operator CRD installation. Applies only when generating a new config.</param>
    /// <param name="showPermissionsOnly">Print the minimum ClusterRole permissions required for bootstrap and exit.</param>
    /// <returns>Process exit code.</returns>
    [Command("bootstrap")]
    public async Task<int> Bootstrap(
        [HideDefaultValue] string config = "",
        bool fluxcdEnable = false,
        bool fluxcdSkipCrds = false,
        bool pulumiSkipCrds = false,
        bool showPermissionsOnly = false)
    {
        if (showPermissionsOnly)
            return PrintPermissions();

        if (!await connectionGuard.EnsureConnectedAsync())
            return 1;

        var configSource = ResolveConfigSource(config);
        var fluxcdEnableValue = false;
        var fluxcdSkipCrdsValue = false;
        var pulumiSkipCrdsValue = false;

        if (configSource == ConfigSource.Generate)
        {
            fluxcdEnableValue = fluxcdEnable;
            fluxcdSkipCrdsValue = fluxcdSkipCrds;
            pulumiSkipCrdsValue = pulumiSkipCrds;
        }

        var result = await RunBootstrapFlowAsync(
            configSource,
            config,
            fluxcdEnableValue,
            fluxcdSkipCrdsValue,
            pulumiSkipCrdsValue);
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
    /// <param name="fluxcdEnable">Enable installation of FluxCD components.</param>
    /// <param name="fluxcdSkipCrds">Skip FluxCD CRD installation when FluxCD is enabled.</param>
    /// <param name="pulumiSkipCrds">Skip Pulumi operator CRD installation.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> RunBootstrapFlowAsync(
        ConfigSource source,
        string configPath,
        bool fluxcdEnable,
        bool fluxcdSkipCrds,
        bool pulumiSkipCrds)
    {
        return source switch
        {
            ConfigSource.Generate => await RunSetupWizardAsync(fluxcdEnable, fluxcdSkipCrds, pulumiSkipCrds),
            ConfigSource.Existing => await UseExistingConfigAsync(configPath, fluxcdEnable, fluxcdSkipCrds, pulumiSkipCrds),
            _ => Result.Fail("Invalid configuration source.")
        };
    }

    /// <summary>
    /// Runs the interactive setup wizard to generate or edit a configuration file.
    /// </summary>
    /// <param name="fluxcdEnable">Enable installation of FluxCD components.</param>
    /// <param name="fluxcdSkipCrds">Skip FluxCD CRD installation when FluxCD is enabled.</param>
    /// <param name="pulumiSkipCrds">Skip Pulumi operator CRD installation.</param>
    /// <param name="existingConfig">Existing configuration used for prefilled edit mode.</param>
    /// <param name="defaultOutputPath">Default output path to suggest when saving.</param>
    /// <param name="encryptionKey">Optional existing encryption key to reuse.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> RunSetupWizardAsync(
        bool fluxcdEnable,
        bool fluxcdSkipCrds,
        bool pulumiSkipCrds,
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
            operatorInfoProvider,
            fluxcdInfoProvider,
            fluxcdEnable,
            fluxcdSkipCrds,
            pulumiSkipCrds);
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
    /// <param name="fluxcdEnable">Enable installation of FluxCD components.</param>
    /// <param name="fluxcdSkipCrds">Skip FluxCD CRD installation when FluxCD is enabled.</param>
    /// <param name="pulumiSkipCrds">Skip Pulumi operator CRD installation.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> UseExistingConfigAsync(
        string? configPath,
        bool fluxcdEnable,
        bool fluxcdSkipCrds,
        bool pulumiSkipCrds)
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
                EnsurePulumiImageVersions(opConfig);
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

        return await TryLoadConfigWithRetries(
            filePath,
            MaxEncryptionKeyAttempts,
            fluxcdEnable,
            fluxcdSkipCrds,
            pulumiSkipCrds);
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
        ConsoleErrorPrinter.PrintError("Invalid encryption key or corrupted configuration file.");
        AnsiConsole.MarkupLine("[grey]Please try again.[/]\n");
    }

    /// <summary>
    /// Resolves configuration path from arguments or interactive prompt.
    /// </summary>
    private static Result? ResolveConfigPath(string? configPath, out string filePath, out bool fromArgs)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            fromArgs = true;
            if (!File.Exists(configPath))
            {
                ConsoleErrorPrinter.PrintError(
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
                    $"File path [green]({DataPlaneConstants.OperatorConfigFileName})[/]     :")
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
                ConsoleErrorPrinter.PrintError(
                    "Configuration file not found.",
                    $"Provided path: {Markup.Escape(Path.GetFullPath(filePath))}");
                continue;
            }

            filePath = Path.GetFullPath(filePath);
            return null;
        }
    }

    /// <summary>
    /// Loads the encryption key from the environment.
    /// </summary>
    private static (Result? Result, string? EncryptionKey) GetEncryptionKeyFromEnv()
    {
        var encryptionKey = Environment.GetEnvironmentVariable(EnvironmentVariables.OperatorEncryptionKey);
        if (!string.IsNullOrWhiteSpace(encryptionKey))
            return (null, encryptionKey);

        ConsoleErrorPrinter.PrintError("Encryption key is required when using --config.");
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

    /// <summary>
    /// Attempts to load a configuration file with repeated encryption key prompts.
    /// </summary>
    /// <param name="filePath">Path to the encrypted configuration file.</param>
    /// <param name="maxAttempts">Maximum number of decryption attempts.</param>
    /// <param name="fluxcdEnable">Enable installation of FluxCD components.</param>
    /// <param name="fluxcdSkipCrds">Skip FluxCD CRD installation when FluxCD is enabled.</param>
    /// <param name="pulumiSkipCrds">Skip Pulumi operator CRD installation.</param>
    /// <returns>Result describing success or failure.</returns>
    private async Task<Result> TryLoadConfigWithRetries(
        string filePath,
        int maxAttempts,
        bool fluxcdEnable,
        bool fluxcdSkipCrds,
        bool pulumiSkipCrds)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
            try
            {
                string encryptionKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("Encryption key for this configuration       :").Secret());
                var opConfig = configStore.LoadConfig(filePath, encryptionKey);
                EnsurePulumiImageVersions(opConfig);
                logger.LogInformation("Loaded configuration from {FilePath}.", filePath);
                var editExisting = AnsiConsole.Prompt(
                    new TextPrompt<bool>("Edit existing configuration? [blue][[y/n]][/] [green](n)[/]      :")
                        .AddChoice(true)
                        .AddChoice(false)
                        .DefaultValue(false)
                        .ShowChoices(false)
                        .ShowDefaultValue(false)
                        .WithConverter(choice => choice ? "y" : "n"));
                if (editExisting)
                {
                    AnsiConsole.WriteLine();
                    return await RunSetupWizardAsync(
                        fluxcdEnable,
                        fluxcdSkipCrds,
                        pulumiSkipCrds,
                        opConfig,
                        filePath,
                        encryptionKey);
                }
                await BootstrapWithStatusAsync(opConfig);
                return Result.Ok();
            }
            catch (CryptographicException)
            {
                if (attempt == maxAttempts)
                {
                    ConsoleErrorPrinter.PrintError(
                        "Too many failed attempts. Unable to decrypt the configuration file.");
                    return Result.Fail();
                }
                PrintInvalidEncryptionKeyMessage();
            }

        return Result.Fail();
    }

    /// <summary>
    /// Ensures Pulumi image versions are populated when missing.
    /// </summary>
    private void EnsurePulumiImageVersions(OperatorConfig opConfig)
    {
        if (!string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.RuntimeVersion) &&
            !string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.PluginsVersion))
        {
            return;
        }

        var info = operatorInfoProvider.GetInfo();
        if (string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.RuntimeVersion))
            opConfig.Pulumi.Images.RuntimeVersion = info.RuntimeVersion;
        if (string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.PluginsVersion))
            opConfig.Pulumi.Images.PluginsVersion = info.PluginsVersion;
    }

    /// <summary>
    /// Prints the minimum ClusterRole permissions required for bootstrap.
    /// </summary>
    private int PrintPermissions()
    {
        string namespaceName, cniNamespace, ciPipelinesNamespace;
        if (System.Console.IsInputRedirected || !Environment.UserInteractive)
        {
            namespaceName = DataPlaneConstants.DefaultSystemNamespace;
            cniNamespace = DataPlaneConstants.DefaultCniNamespace;
            ciPipelinesNamespace = DataPlaneConstants.DefaultCiPipelinesNamespace;
        }
        else
        {
            namespaceName = NamespacePrompts.PromptSystemNamespace();
            cniNamespace = NamespacePrompts.PromptCniNamespace();
            ciPipelinesNamespace = NamespacePrompts.PromptCiPipelinesNamespace();
        }

        var serviceAccountName = DataPlaneConstants.ServiceAccountName;

        // Build a minimal config for the recording run
        var opConfig = new OperatorConfig();
        opConfig.Kubernetes.Namespaces.System.Name = namespaceName;
        opConfig.Kubernetes.Namespaces.Cni.Name = cniNamespace;
        opConfig.Kubernetes.Namespaces.CiPipelines.Name = ciPipelinesNamespace;

        // Run bootstrap against a recording client to capture all operations
        var recordingClient = new Adapters.Kubernetes.RecordingKubernetesClient();
        var recordingFactory = new Adapters.Kubernetes.RecordingKubernetesClientFactory(recordingClient);
        var recordingBootstrap = new BootstrapService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BootstrapService>.Instance,
            recordingFactory,
            new Adapters.Kubernetes.KubernetesConnectionOptions(),
            new NamespaceProvisioner(Microsoft.Extensions.Logging.Abstractions.NullLogger<NamespaceProvisioner>.Instance),
            new FluxCDProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FluxCDProvisioner>.Instance,
                new Common.Yaml.YamlToJsonConverter(),
                new FluxCDInfoProvider()),
            new PulumiOperatorProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiOperatorProvisioner>.Instance,
                new Common.Yaml.YamlToJsonConverter(),
                new DataPlaneConfigSecretBuilder(),
                new PulumiOperatorInfoProvider()),
            new PulumiStackProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiStackProvisioner>.Instance,
                new PulumiStackManifestBuilder(
                    new PulumiOperatorProvisioner(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<PulumiOperatorProvisioner>.Instance,
                        new Common.Yaml.YamlToJsonConverter(),
                        new DataPlaneConfigSecretBuilder(),
                        new PulumiOperatorInfoProvider()),
                    new PulumiOperatorInfoProvider())));

        recordingBootstrap.BootstrapAsync(opConfig).GetAwaiter().GetResult();

        var deployerClusterRole = BootstrapManifestPrinter.BuildDeployerClusterRole(
            serviceAccountName, recordingClient.Operations, recordingClient.AppliedObjects);

        var yaml = BootstrapManifestPrinter.ToYaml([deployerClusterRole]);
        System.Console.Write(yaml);
        return 0;
    }
}
