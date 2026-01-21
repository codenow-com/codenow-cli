using CodeNOW.Cli.DataPlane.Console.Commands;
using CodeNOW.Cli.DataPlane.Console.Filters;
using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.DataPlane.Services.Provisioning;
using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.Common.Console.Presentation;
using CodeNOW.Cli.Common.Yaml;
using ConsoleAppFramework;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Lunet.Extensions.Logging.SpectreConsole;
using System.Globalization;
using System.Reflection;

namespace CodeNOW.Cli;

internal record GlobalOptions(bool Verbose, string? KubeProxyUrl);

/// <summary>
/// CLI entry point.
/// </summary>
public static class Program
{
    private const string DefaultKubeProxyUrl = "http://127.0.0.1:8001";
    private const string DocumentationUrlTemplate = "https://codenow-com.github.io/codenow-cli/docs/v{0}/";

    /// <summary>
    /// Application entry point.
    /// </summary>
    public static void Main(string[] args)

    {
        // KubernetesConnectionFilter enables graceful shutdown; shorten the default 5s timeout.
        ConsoleApp.Timeout = TimeSpan.FromSeconds(1);
        EnsureConsoleAppVersion();
        AnsiConsole.Clear();
        var bannerPolicy = new BannerVisibilityPolicy(
            [
                new CommandDescriptor("dp", "bootstrap", HideBanner: false),
                new CommandDescriptor("dp", "dashboard", HideBanner: true)
            ]);
        if (bannerPolicy.ShouldShowBanner(args))
        {
            var banner = @"
[#00FFFF]   ______          __     _   ______  _       __[/]
[#00E0FF]  / ____/___  ____/ /__  / | / / __ \| |     / /[/]
[#00FFFF] / /   / __ \/ __  / _ \/  |/ / / / /| | /| / / [/]
[#00E0FF]/ /___/ /_/ / /_/ /  __/ /|  / /_/ / | |/ |/ /  [/]
[#00FFFF]\____/\____/\__,_/\___/_/ |_/\____/  |__/|__/   [/]
[#00E0FF]         Cloud-native Delivery Platform         [/]
";
            AnsiConsole.Write(new Markup(banner));
            AnsiConsole.WriteLine();
        }

        var app = ConsoleApp.Create();

        ConfigureHelpFooter();
        ShowExternalDocLink(args);

        app.ConfigureGlobalOptions((ref builder) =>
        {
            var verbose = builder.AddGlobalOption<bool>("--verbose", "Enable verbose logging.");
            var proxyUrl = builder.AddGlobalOption<string>(
                "--kube-proxy-url",
                "HTTP URL of the local kubectl proxy.",
                DefaultKubeProxyUrl);
            return new GlobalOptions(verbose, proxyUrl);
        });
        app.ConfigureServices((context, services) =>
        {
            var globalOptions = (GlobalOptions)(context.GlobalOptions ?? new GlobalOptions(false, null));
            services.AddSingleton(globalOptions);
            services.AddSingleton(new KubernetesConnectionOptions
            {
                ProxyUrl = globalOptions.KubeProxyUrl
            });
            services.AddSingleton<IKubernetesClientFactory, KubernetesClientFactoryAdapter>();
            services.AddSingleton<YamlToJsonConverter>();
            services.AddSingleton<IOperatorInfoProvider, OperatorInfoProvider>();
            services.AddSingleton<INamespaceProvisioner, NamespaceProvisioner>();
            services.AddSingleton<IPulumiOperatorProvisioner, PulumiOperatorProvisioner>();
            services.AddSingleton<DataPlaneConfigSecretBuilder>();
            services.AddSingleton<PulumiStackManifestBuilder>();
            services.AddSingleton<IPulumiStackProvisioner, PulumiStackProvisioner>();
            services.AddSingleton<IBootstrapService, BootstrapService>();
            services.AddSingleton<IManagementService, ManagementService>();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(globalOptions.Verbose ? LogLevel.Information : LogLevel.Error);
                logging.AddSpectreConsole(new SpectreConsoleLoggerOptions
                {
                    IncludeCategory = false,
                    SingleLine = true,
                    IncludeNewLineBeforeMessage = false,
                    LogLevel = globalOptions.Verbose ? LogLevel.Information : LogLevel.Error
                });
            });
        });

        app.UseFilter<KubernetesConnectionFilter>();
        app.Add<BootstrapCommand>("dp");
        app.Add<DashboardCommand>("dp");
        app.Run(args);
    }

    /// <summary>
    /// Hooks into ConsoleAppFramework help output to insert a blank line after Usage.
    /// </summary>
    private static void ConfigureHelpFooter()
    {
        var docUrl = BuildDocumentationUrl();
        if (string.IsNullOrWhiteSpace(docUrl))
            return;

        var injected = false;
        var originalLog = ConsoleApp.Log;
        ConsoleApp.Log = message =>
        {
            originalLog(message);

            if (injected)
                return;

            if (!message.TrimStart().StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                return;

            injected = true;
            AnsiConsole.WriteLine();
        };
    }

    /// <summary>
    /// Shows the external documentation link on root/--help invocation.
    /// </summary>
    private static void ShowExternalDocLink(string[] args)
    {
        var hasHelpFlag = args.Any(token => token is "-h" or "--help");
        var nonOptionTokens = args
            .Where(token => !string.IsNullOrWhiteSpace(token) && !token.StartsWith("-", StringComparison.Ordinal))
            .ToArray();
        if (!hasHelpFlag && nonOptionTokens.Length != 0)
            return;

        var docUrl = BuildDocumentationUrl();
        if (string.IsNullOrWhiteSpace(docUrl))
            return;

        AnsiConsole.MarkupLine($"Documentation: [link={docUrl}]{docUrl}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Builds the documentation URL for the current CLI major/minor version.
    /// </summary>
    private static string BuildDocumentationUrl()
    {
        var version = GetCliVersion();
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        return string.Format(CultureInfo.InvariantCulture, DocumentationUrlTemplate, version);
    }

    /// <summary>
    /// Reads the CLI version from ConsoleAppFramework.
    /// </summary>
    private static string GetCliVersion()
    {
        var versionText = ConsoleApp.Version;
        if (string.IsNullOrWhiteSpace(versionText))
            return string.Empty;

        return ExtractMajorMinor(versionText);
    }

    /// <summary>
    /// Extracts the major/minor portion from a semantic version string.
    /// </summary>
    private static string ExtractMajorMinor(string versionText)
    {
        var normalized = versionText.Split('+', 2)[0];
        normalized = normalized.Split('-', 2)[0];
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return normalized;

        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor))
        {
            return $"{major}.{minor}";
        }

        return $"{parts[0]}.{parts[1]}";
    }

    /// <summary>
    /// Ensures ConsoleAppFramework has a version set, using assembly metadata as fallback.
    /// </summary>
    private static void EnsureConsoleAppVersion()
    {
        if (!string.IsNullOrWhiteSpace(ConsoleApp.Version))
            return;

        var assembly = Assembly.GetEntryAssembly();
        var version = "latest";
        if (assembly is not null)
        {
            var infoVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
            if (infoVersion is not null && !string.IsNullOrWhiteSpace(infoVersion.InformationalVersion))
            {
                version = infoVersion.InformationalVersion;
                var separator = version.IndexOf('+');
                if (separator != -1)
                    version = version.Substring(0, separator);
            }
            else
            {
                var asmVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyVersionAttribute>();
                if (asmVersion is not null && !string.IsNullOrWhiteSpace(asmVersion.Version))
                    version = asmVersion.Version;
            }
        }

        ConsoleApp.Version = version;
    }
}
