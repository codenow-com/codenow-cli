using System.Text;
using CodeNOW.Cli.Common.Console.Commands;
using CodeNOW.Cli.Common.Console.Prompts;
using CodeNOW.Cli.DataPlane.Console.Supports;
using CodeNOW.Cli.DataPlane.Models;
using Spectre.Console;
using TextCopy;

namespace CodeNOW.Cli.DataPlane.Console.Prompts;

/// <summary>
/// Interactive wizard for creating operator configuration.
/// </summary>
internal sealed class BootstrapWizard
{
    /// <summary>
    /// Runs the configuration wizard and persists the result to disk.
    /// </summary>
    public (Result Result, OperatorConfig? Config) Run(
        OperatorConfig? existingConfig,
        string? defaultOutputPath,
        string? encryptionKey,
        string defaultConfigFile,
        BootstrapConfigStore configStore,
        PulumiOperatorInfoProvider operatorInfoProvider)
    {
        var opConfig = existingConfig ?? new OperatorConfig();
        var usePrefill = existingConfig is not null;
        var promptFactory = new PromptFactory(usePrefill);

        PromptEnvironment(promptFactory, opConfig, usePrefill);
        PromptNpmRegistry(promptFactory, opConfig, usePrefill);
        PromptContainerRegistry(promptFactory, opConfig, usePrefill);
        PromptScm(promptFactory, opConfig, existingConfig);
        PromptKubernetes(promptFactory, opConfig, usePrefill, existingConfig);
        PromptS3(promptFactory, opConfig, usePrefill, existingConfig);
        PromptHttpProxy(promptFactory, opConfig, usePrefill);
        PromptSecurity(promptFactory, opConfig, usePrefill);
        var outputPath = PromptOutputPath(promptFactory, defaultConfigFile, defaultOutputPath, usePrefill);
        if (string.IsNullOrWhiteSpace(outputPath))
            return (Result.Fail(), null);

        var showGeneratedKey = false;
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            encryptionKey = configStore.GenerateEncryptionKey();
            showGeneratedKey = true;
        }

        if (existingConfig is null || string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.RuntimeVersion))
            opConfig.Pulumi.Images.RuntimeVersion = operatorInfoProvider.LoadRuntimeImageVersion();
        if (existingConfig is null || string.IsNullOrWhiteSpace(opConfig.Pulumi.Images.PluginsVersion))
            opConfig.Pulumi.Images.PluginsVersion = operatorInfoProvider.LoadPluginsImageVersion();
        if (existingConfig is null || string.IsNullOrWhiteSpace(opConfig.Pulumi.Passphrase))
            opConfig.Pulumi.Passphrase = configStore.GeneratePulumiPassphrase();

        opConfig.Schema = DataPlaneConstants.OperatorConfigSchemaV1Url;

        configStore.SaveConfig(outputPath, opConfig, encryptionKey);

        AnsiConsole.MarkupLine($"[grey]Configuration saved to: {Markup.Escape(outputPath)}[/]");

        if (showGeneratedKey)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]An encryption key has been generated.[/]\n");
            AnsiConsole.Write(
                new Panel(encryptionKey)
                    .Header("Encryption Key", Justify.Center)
                    .Border(BoxBorder.Rounded)
                    .BorderStyle(new Style(Color.Yellow)));
            try
            {
                ClipboardService.SetText(encryptionKey);
                AnsiConsole.MarkupLine(
                    "\n[green]✔ Encryption key copied to the clipboard.[/]");
            }
            catch
            {
                AnsiConsole.MarkupLine(
                    "\n[grey]Clipboard not available. Please copy the key manually.[/]");
            }
            AnsiConsole.MarkupLine(
                "\n[grey]Store it in a password manager or a secure secret store. It will not be shown again.[/]\n");
            AnsiConsole.Markup("[grey]Press Enter to continue...[/]\n");
            while (true)
            {
                var key = System.Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                    break;
            }
        }
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        return (Result.Ok(), opConfig);
    }

    private static void PromptEnvironment(PromptFactory promptFactory, OperatorConfig opConfig, bool usePrefill)
    {
        WriteSection("Environment");
        opConfig.Environment.Name = PromptText(
            promptFactory,
            "Environment name                                           :",
            initialValue: usePrefill ? opConfig.Environment.Name : null);
        AnsiConsole.WriteLine();
    }

    private static void PromptNpmRegistry(PromptFactory promptFactory, OperatorConfig opConfig, bool usePrefill)
    {
        WriteSection("NPM Registry");
        opConfig.NpmRegistry.Url = PromptText(
            promptFactory,
            "URL                                                        :",
            initialValue: usePrefill ? opConfig.NpmRegistry.Url : null,
            validator: PromptValidators.ValidateUrl);
        opConfig.NpmRegistry.AccessToken = PromptSecret(
            promptFactory,
            "Access token                                               :",
            initialValue: usePrefill ? opConfig.NpmRegistry.AccessToken : null);
        AnsiConsole.WriteLine();
    }

    private static void PromptContainerRegistry(PromptFactory promptFactory, OperatorConfig opConfig, bool usePrefill)
    {
        WriteSection("Container Registry");
        opConfig.ContainerRegistry.Hostname = PromptText(
            promptFactory,
            "Hostname                                                   :",
            initialValue: usePrefill ? opConfig.ContainerRegistry.Hostname : null);
        opConfig.ContainerRegistry.Username = PromptText(
            promptFactory,
            "Username                                                   :",
            initialValue: usePrefill ? opConfig.ContainerRegistry.Username : null);
        opConfig.ContainerRegistry.Password = PromptSecret(
            promptFactory,
            "Password                                                   :",
            initialValue: usePrefill ? opConfig.ContainerRegistry.Password : null);
        AnsiConsole.WriteLine();
    }

    private static void PromptScm(
        PromptFactory promptFactory,
        OperatorConfig opConfig,
        OperatorConfig? existingConfig)
    {
        WriteSection("SCM (Git)");
        opConfig.Scm.Url = PromptText(
            promptFactory,
            "Configuration repository URL                               :",
            initialValue: existingConfig is not null ? opConfig.Scm.Url : null,
            validator: PromptValidators.ValidateUrl);
        var scmAuthChoices = PromptFactory.BuildSelectionChoices(
            existingConfig is not null ? opConfig.Scm.AuthenticationMethod : null,
            ScmAuthenticationMethod.UsernamePassword,
            ScmAuthenticationMethod.AccessToken);
        opConfig.Scm.AuthenticationMethod = AnsiConsole.Prompt(
            new SelectionPrompt<ScmAuthenticationMethod>()
                .Title("Authentication method?")
                .UseConverter(m => m switch
                {
                    ScmAuthenticationMethod.UsernamePassword => "Username / password",
                    ScmAuthenticationMethod.AccessToken => "Access token",
                    _ => m.ToString()
                })
                .AddChoices(scmAuthChoices)
        );

        switch (opConfig.Scm.AuthenticationMethod)
        {
            case ScmAuthenticationMethod.UsernamePassword:
                opConfig.Scm.Username = PromptText(
                    promptFactory,
                    "Username                                                   :",
                    initialValue: existingConfig is not null ? opConfig.Scm.Username : null);
                opConfig.Scm.Password = PromptSecret(
                    promptFactory,
                    "Password                                                   :",
                    initialValue: existingConfig is not null ? opConfig.Scm.Password : null);
                break;

            case ScmAuthenticationMethod.AccessToken:
                opConfig.Scm.AccessToken = PromptSecret(
                    promptFactory,
                    "Access token                                               :",
                    initialValue: existingConfig is not null ? opConfig.Scm.AccessToken : null);
                break;
        }
        AnsiConsole.WriteLine();
    }

    private static void PromptKubernetes(
        PromptFactory promptFactory,
        OperatorConfig opConfig,
        bool usePrefill,
        OperatorConfig? existingConfig)
    {
        WriteSection("Kubernetes");
        AnsiConsole.MarkupLine("[grey]Namespaces[/]");
        opConfig.Kubernetes.Namespaces.System.Name = PromptText(
            promptFactory,
            "• System [green](cn-data-plane-system)[/]                            :",
            initialValue: usePrefill ? opConfig.Kubernetes.Namespaces.System.Name : null,
            defaultValue: "cn-data-plane-system",
            showDefaultValue: false);
        opConfig.Kubernetes.Namespaces.Cni.Name = PromptText(
            promptFactory,
            "• CNI [green](cn-data-plane-cni)[/]                                  :",
            initialValue: usePrefill ? opConfig.Kubernetes.Namespaces.Cni.Name : null,
            defaultValue: "cn-data-plane-cni",
            showDefaultValue: false);
        opConfig.Kubernetes.Namespaces.CiPipelines.Name = PromptText(
            promptFactory,
            "• CI Pipelines [green](cn-data-plane-ci-pipelines)[/]                :",
            initialValue: usePrefill ? opConfig.Kubernetes.Namespaces.CiPipelines.Name : null,
            defaultValue: "cn-data-plane-ci-pipelines",
            showDefaultValue: false);
        AnsiConsole.MarkupLine("[grey]Node labels[/]");
        opConfig.Kubernetes.NodeLabels.System.Key = PromptText(
            promptFactory,
            "• System key [green](node-restriction.kubernetes.io/reserved-for)[/] :",
            initialValue: usePrefill ? opConfig.Kubernetes.NodeLabels.System.Key : null,
            defaultValue: "node-restriction.kubernetes.io/reserved-for",
            showDefaultValue: false);
        opConfig.Kubernetes.NodeLabels.System.Value = PromptText(
            promptFactory,
            "• System value [green](cn-data-plane-system)[/]                      :",
            initialValue: usePrefill ? opConfig.Kubernetes.NodeLabels.System.Value : null,
            defaultValue: "cn-data-plane-system",
            showDefaultValue: false);
        opConfig.Kubernetes.NodeLabels.Application.Key = PromptText(
            promptFactory,
            "• App key [green](node-restriction.kubernetes.io/reserved-for)[/]    :",
            initialValue: usePrefill ? opConfig.Kubernetes.NodeLabels.Application.Key : null,
            defaultValue: "node-restriction.kubernetes.io/reserved-for",
            showDefaultValue: false);
        opConfig.Kubernetes.NodeLabels.Application.Value = PromptText(
            promptFactory,
            "• App value [green](cn-application)[/]                               :",
            initialValue: usePrefill ? opConfig.Kubernetes.NodeLabels.Application.Value : null,
            defaultValue: "cn-application",
            showDefaultValue: false);
        AnsiConsole.MarkupLine("[grey]Pod placement[/]");
        var podPlacementChoices = PromptFactory.BuildSelectionChoices(
            existingConfig is not null ? opConfig.Kubernetes.PodPlacementMode : null,
            PodPlacementMode.PodNodeSelector,
            PodPlacementMode.NodeSelectorAndTaints);
        opConfig.Kubernetes.PodPlacementMode = AnsiConsole.Prompt(
            new SelectionPrompt<PodPlacementMode>()
                .Title("• Mode?")
                .UseConverter(m =>
                    m switch
                    {
                        PodPlacementMode.PodNodeSelector => "Pod node selector",
                        PodPlacementMode.NodeSelectorAndTaints => "Node selector + taints",
                        _ => m.ToString()
                    }
                )
                .AddChoices(podPlacementChoices)
        );
        string display = opConfig.Kubernetes.PodPlacementMode switch
        {
            PodPlacementMode.PodNodeSelector => "Pod node selector",
            PodPlacementMode.NodeSelectorAndTaints => "Node selector + taints",
            _ => opConfig.Kubernetes.PodPlacementMode.ToString()
        };
        AnsiConsole.Write("• Mode                                                     : " + display);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Storage[/]");
        var storageClass = PromptText(
            promptFactory,
            "• Storage class [green](default)[/]                                  :",
            initialValue: usePrefill ? opConfig.Kubernetes.StorageClass : null,
            showDefaultValue: false,
            allowEmpty: true);
        opConfig.Kubernetes.StorageClass = string.IsNullOrWhiteSpace(storageClass) ? null : storageClass;
        AnsiConsole.MarkupLine("[grey]Security context[/]");
        opConfig.Kubernetes.SecurityContextRunAsId = PromptInt(
            promptFactory,
            "• RunAs UID/GID [green](1000)[/]                                    :",
            initialValue: usePrefill ? opConfig.Kubernetes.SecurityContextRunAsId : null,
            defaultValue: 1000,
            showDefaultValue: false);
        AnsiConsole.WriteLine();
    }

    private static void PromptS3(
        PromptFactory promptFactory,
        OperatorConfig opConfig,
        bool usePrefill,
        OperatorConfig? existingConfig)
    {
        WriteSection("S3 Storage");
        opConfig.S3.Enabled = PromptBool(
            promptFactory,
            "Enable? [blue][[y/n]][/] [green](y)[/]                                          :",
            initialValue: usePrefill ? opConfig.S3.Enabled : null,
            defaultValue: true,
            showDefaultValue: false,
            showChoices: false);
        if (opConfig.S3.Enabled)
        {
            opConfig.S3.Url = PromptText(
                promptFactory,
                "URL                                                        :",
                initialValue: usePrefill ? opConfig.S3.Url : null,
                validator: PromptValidators.ValidateUrl);
            opConfig.S3.Bucket = PromptText(
                promptFactory,
                "Bucket name                                                : ",
                initialValue: usePrefill ? opConfig.S3.Bucket : null);
            opConfig.S3.Region = PromptText(
                promptFactory,
                "Region [green](eu-central-1)[/]                                      :",
                initialValue: usePrefill ? opConfig.S3.Region : null,
                defaultValue: "eu-central-1",
                showDefaultValue: false);

            var s3AuthChoices = PromptFactory.BuildSelectionChoices(
                existingConfig is not null ? opConfig.S3.AuthenticationMethod : null,
                S3AuthenticationMethod.AccessKeySecretKey,
                S3AuthenticationMethod.IAMRole);
            opConfig.S3.AuthenticationMethod = AnsiConsole.Prompt(
                new SelectionPrompt<S3AuthenticationMethod>()
                    .Title("Authentication method?")
                    .UseConverter(m => m switch
                    {
                        S3AuthenticationMethod.AccessKeySecretKey => "Access Key / Secret Key",
                        S3AuthenticationMethod.IAMRole => "IAM Role",
                        _ => m.ToString()
                    })
                    .AddChoices(s3AuthChoices)
            );

            switch (opConfig.S3.AuthenticationMethod)
            {
                case S3AuthenticationMethod.AccessKeySecretKey:
                    opConfig.S3.AccessKey = PromptText(
                        promptFactory,
                        "Access key                                                 :",
                        initialValue: usePrefill ? opConfig.S3.AccessKey : null);
                    opConfig.S3.SecretKey = PromptSecret(
                        promptFactory,
                        "Secret key                                                 :",
                        initialValue: usePrefill ? opConfig.S3.SecretKey : null);
                    break;

                case S3AuthenticationMethod.IAMRole:
                    opConfig.S3.IAMRole = PromptText(
                        promptFactory,
                        "IAM role                                                   :",
                        initialValue: usePrefill ? opConfig.S3.IAMRole : null,
                        validator: PromptValidators.ValidateIamRoleArnPrefix);
                    break;
            }
        }
        AnsiConsole.WriteLine();
    }

    private static void PromptHttpProxy(PromptFactory promptFactory, OperatorConfig opConfig, bool usePrefill)
    {
        WriteSection("HTTP Proxy");
        opConfig.HttpProxy.Enabled = PromptBool(
            promptFactory,
            "Enable? [blue][[y/n]][/] [green](n)[/]                                          :",
            initialValue: usePrefill ? opConfig.HttpProxy.Enabled : null,
            defaultValue: false,
            showDefaultValue: false,
            showChoices: false);
        if (opConfig.HttpProxy.Enabled)
        {
            opConfig.HttpProxy.Hostname = PromptText(
                promptFactory,
                "Hostname                                                   :",
                initialValue: usePrefill ? opConfig.HttpProxy.Hostname : null);
            opConfig.HttpProxy.Port = PromptInt(
                promptFactory,
                "Port [green](8080)[/]                                                :",
                initialValue: usePrefill ? opConfig.HttpProxy.Port : null,
                defaultValue: 8080,
                showDefaultValue: false);
            opConfig.HttpProxy.NoProxy = PromptText(
                promptFactory,
                "No proxy hostnames                                         :",
                initialValue: usePrefill ? opConfig.HttpProxy.NoProxy : null,
                allowEmpty: true);
        }
        AnsiConsole.WriteLine();
    }

    private static void PromptSecurity(PromptFactory promptFactory, OperatorConfig opConfig, bool usePrefill)
    {
        WriteSection("Security");
        var customCaEnabled = PromptBool(
            promptFactory,
            "Use custom CA? [blue][[y/n]][/] [green](n)[/]                                   :",
            initialValue: usePrefill && !string.IsNullOrWhiteSpace(opConfig.Security.CustomCaBase64),
            defaultValue: false,
            showDefaultValue: false,
            showChoices: false);
        if (customCaEnabled)
        {
            var caPath = PromptText(
                promptFactory,
                "Custom CA certificate path (.crt/.pem):                    :",
                allowEmpty: usePrefill && !string.IsNullOrWhiteSpace(opConfig.Security.CustomCaBase64),
                validator: path =>
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        if (usePrefill && !string.IsNullOrWhiteSpace(opConfig.Security.CustomCaBase64))
                            return ValidationResult.Success();
                        return ValidationResult.Error("[red]Error: Path cannot be empty.[/]\n");
                    }
                    if (!File.Exists(path))
                        return ValidationResult.Error(
                            $"[red]Error: File not found.\n" +
                            $"Provided path: {Markup.Escape(Path.GetFullPath(path))}[/]\n");
                    var caPemContent = File.ReadAllText(path);
                    if (!caPemContent.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
                        return ValidationResult.Error("[red]Error: File does not appear to contain a PEM certificate.[/]\n");
                    return ValidationResult.Success();
                });
            if (!string.IsNullOrWhiteSpace(caPath))
            {
                var caPem = File.ReadAllText(caPath);
                opConfig.Security.CustomCaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(caPem));
            }
        }
        else
        {
            opConfig.Security.CustomCaBase64 = null;
        }
        AnsiConsole.WriteLine();
    }

    private static string? PromptOutputPath(
        PromptFactory promptFactory,
        string defaultConfigFile,
        string? defaultOutputPath,
        bool usePrefill)
    {
        WriteSection("Configuration Output");
        AnsiConsole.MarkupLine("[grey]Choose where to save the configuration file.[/]\n");
        var outputPath = PromptText(
            promptFactory,
            $"Configuration file path [green]({defaultConfigFile})[/]      :",
            initialValue: usePrefill ? defaultOutputPath : null,
            defaultValue: defaultConfigFile,
            showDefaultValue: false,
            validator: path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                    return ValidationResult.Error("[red]Error: Path cannot be empty.[/]\n");

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    return ValidationResult.Error("[red]Error: Directory does not exist.[/]\n");

                return ValidationResult.Success();
            });
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath))
        {
            var overwrite = PromptBool(
                promptFactory,
                "File already exists. Overwrite? [blue][[y/n]][/] [green](n)[/]                  :",
                defaultValue: false,
                showDefaultValue: false,
                showChoices: false);

            if (!overwrite)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]Configuration was not saved.[/]\n");
                return null;
            }
        }

        return outputPath;
    }

    private static void WriteSection(string title)
    {
        AnsiConsole.Write(new Rule($"[yellow]{title}[/]").RuleStyle("grey").LeftJustified());
    }

    private static string PromptText(
        PromptFactory promptFactory,
        string prompt,
        string? initialValue = null,
        string? defaultValue = null,
        bool showDefaultValue = true,
        bool allowEmpty = false,
        Func<string, ValidationResult>? validator = null)
    {
        return AnsiConsole.Prompt(promptFactory.CreateStringPrompt(
            prompt,
            initialValue: initialValue,
            defaultValue: defaultValue,
            showDefaultValue: showDefaultValue,
            allowEmpty: allowEmpty,
            validator: validator));
    }

    private static string PromptSecret(
        PromptFactory promptFactory,
        string prompt,
        string? initialValue = null,
        string? defaultValue = null,
        bool showDefaultValue = true,
        bool allowEmpty = false,
        Func<string, ValidationResult>? validator = null)
    {
        return AnsiConsole.Prompt(promptFactory.CreateStringPrompt(
            prompt,
            initialValue: initialValue,
            defaultValue: defaultValue,
            showDefaultValue: showDefaultValue,
            allowEmpty: allowEmpty,
            secret: true,
            validator: validator));
    }

    private static bool PromptBool(
        PromptFactory promptFactory,
        string prompt,
        bool? initialValue = null,
        bool? defaultValue = null,
        bool showDefaultValue = true,
        bool showChoices = true)
    {
        return AnsiConsole.Prompt(promptFactory.CreateBoolPrompt(
            prompt,
            initialValue: initialValue,
            defaultValue: defaultValue,
            showDefaultValue: showDefaultValue,
            showChoices: showChoices));
    }

    private static int PromptInt(
        PromptFactory promptFactory,
        string prompt,
        int? initialValue = null,
        int? defaultValue = null,
        bool showDefaultValue = true)
    {
        return AnsiConsole.Prompt(promptFactory.CreateIntPrompt(
            prompt,
            initialValue: initialValue,
            defaultValue: defaultValue,
            showDefaultValue: showDefaultValue));
    }
}
