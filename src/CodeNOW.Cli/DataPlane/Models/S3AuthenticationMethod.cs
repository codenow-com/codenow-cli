using System.Text.Json.Serialization;

namespace CodeNOW.Cli.DataPlane.Models;

/// <summary>
/// Supported authentication methods for S3 access.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<S3AuthenticationMethod>))]
public enum S3AuthenticationMethod
{
    /// <summary>
    /// Access key and secret key pair.
    /// </summary>
    AccessKeySecretKey,
    /// <summary>
    /// IAM role-based authentication.
    /// </summary>
    IAMRole
}
