using System.Globalization;
using System.Security.Cryptography;

namespace CodeNOW.Cli.DataPlane.Models;

internal static class ScmConfigValidator
{
    public static bool IsValidId(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;

    public static bool IsValidPrivateKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            // The operator expects a PKCS#1 RSA private key.
            var pem = PemEncoding.Find(value);
            if (value[pem.Label] != "RSA PRIVATE KEY")
                return false;
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(value[pem.Base64Data]), out _);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException)
        {
            return false;
        }
    }

    public static bool IsValidApiUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    public static void Validate(OperatorConfig config)
    {
        var scm = config.Scm;
        if (scm.AuthenticationMethod != ScmAuthenticationMethod.GitHubApp)
            return;
        if (!IsValidId(scm.GithubAppId) || !IsValidId(scm.GithubAppInstallationId))
            throw new ArgumentException("GitHub App ID and installation ID must be positive integers.");
        if (!IsValidPrivateKey(scm.GithubAppPrivateKey))
            throw new ArgumentException("GitHub App private key must be a PEM-encoded PKCS#1 RSA private key.");
        if (!string.IsNullOrWhiteSpace(scm.GithubApiUrl))
        {
            if (!IsValidApiUrl(scm.GithubApiUrl))
                throw new ArgumentException("GitHub API URL must be an absolute HTTPS URL without credentials, query or fragment.");
            if (config.FluxCD?.Enabled != true &&
                !string.Equals(scm.GithubApiUrl.TrimEnd('/'), "https://api.github.com", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("GitHub Enterprise API URL requires FluxCD (--fluxcd-enable).");
        }
    }
}
