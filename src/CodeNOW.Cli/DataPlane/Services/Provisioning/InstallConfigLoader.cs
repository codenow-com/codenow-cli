using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeNOW.Cli.DataPlane.Models;

namespace CodeNOW.Cli.DataPlane.Services.Provisioning;

/// <summary>Raised for invalid installation input. Messages name fields, never their values.</summary>
public sealed class InstallConfigException(string message) : Exception(message);

/// <summary>Parses and validates the installation document before any cluster access.</summary>
public static partial class InstallConfigLoader
{
    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex DnsLabel();

    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*$")]
    private static partial Regex DnsSubdomain();

    [GeneratedRegex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*|\[[0-9a-fA-F:.]+\])(?::(?<port>[0-9]{1,5}))?$")]
    private static partial Regex RegistryHost();

    /// <summary>Reads a file and returns the validated document with its namespace default applied.</summary>
    public static InstallConfig Load(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallConfigException($"Cannot read installation configuration: {path}");
        }

        return Parse(json);
    }

    /// <summary>Parses and validates a document; JSON Schema defaults are applied explicitly here.</summary>
    public static InstallConfig Parse(string json)
    {
        InstallConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallConfigJsonContext.Default.InstallConfig);
        }
        catch (JsonException ex)
        {
            // The path is safe; the message can quote input values.
            throw new InstallConfigException($"Installation configuration is invalid at {ex.Path ?? "the document root"}.");
        }

        if (config is null)
        {
            throw new InstallConfigException("Installation configuration is empty.");
        }

        Validate(config);
        return config with { NamespaceName = config.NamespaceName ?? DataPlaneConstants.DefaultSystemNamespace };
    }

    private static void Validate(InstallConfig config)
    {
        if (!Uri.TryCreate(config.ControlPlaneUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttp)
            throw new InstallConfigException("controlPlaneUrl must be an absolute HTTP URL; HTTPS is not supported yet.");

        if (string.IsNullOrEmpty(config.AuthorizationToken))
            throw new InstallConfigException("authorizationToken is required.");

        if (!IsRegistryHostname(config.ContainerRegistrySystemHostname))
            throw new InstallConfigException("containerRegistrySystemHostname is required and must be a DNS name, IPv4 or bracketed IPv6 address with an optional port.");

        var hasSecret = config.ImagePullSecret is not null;
        var hasCredentials = config.ContainerRegistrySystem is not null;
        if (hasSecret == hasCredentials)
            throw new InstallConfigException("Set exactly one of imagePullSecret or containerRegistrySystem.");

        if (hasSecret && (config.ImagePullSecret!.Length > 253 || !DnsSubdomain().IsMatch(config.ImagePullSecret)))
            throw new InstallConfigException("imagePullSecret is not a valid Secret name.");

        if (hasCredentials && (string.IsNullOrEmpty(config.ContainerRegistrySystem!.Username) || string.IsNullOrEmpty(config.ContainerRegistrySystem.Password)))
            throw new InstallConfigException("containerRegistrySystem requires username and password.");

        if (config.NamespaceName is { } name && (name.Length > 63 || !DnsLabel().IsMatch(name)))
            throw new InstallConfigException("namespaceName must be a lowercase DNS label of at most 63 characters.");

        if (config.HttpProxy is { } proxy)
        {
            if (!IsProxyHostname(proxy.Hostname))
                throw new InstallConfigException("httpProxy.hostname must be a hostname or IP address without scheme or port.");

            if (proxy.Port is < 1 or > 65535)
                throw new InstallConfigException("httpProxy.port must be between 1 and 65535.");
        }
    }

    /// <summary>The same rule the Controller applies to persisted configuration.</summary>
    public static bool IsRegistryHostname(string? value)
    {
        if (value is null)
            return false;

        var match = RegistryHost().Match(value);
        if (!match.Success)
            return false;

        var port = match.Groups["port"];
        if (port.Success && int.Parse(port.Value) is < 1 or > 65535)
            return false;

        if (!value.StartsWith('['))
            return true;

        return IPAddress.TryParse(value[1..value.IndexOf(']')], out var address)
            && address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    private static bool IsProxyHostname(string? value) =>
        !string.IsNullOrEmpty(value)
        && (DnsSubdomain().IsMatch(value.ToLowerInvariant()) || IPAddress.TryParse(value, out _));
}
