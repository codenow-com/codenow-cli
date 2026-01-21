using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Supported authentication methods for SCM (Git) access.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScmAuthenticationMethod>))]
public enum ScmAuthenticationMethod
{
    /// <summary>
    /// Username and password authentication.
    /// </summary>
    UsernamePassword,
    /// <summary>
    /// Personal access token authentication.
    /// </summary>
    AccessToken
}
