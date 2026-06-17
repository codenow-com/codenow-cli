using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Supported authentication methods for NPM registry access.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NpmAuthenticationMethod>))]
public enum NpmAuthenticationMethod
{
    /// <summary>
    /// Username and password authentication.
    /// </summary>
    UsernamePassword,

    /// <summary>
    /// Access token authentication.
    /// </summary>
    AccessToken
}
