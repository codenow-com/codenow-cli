namespace CodeNOW.Cli.DataPlane;

/// <summary>
/// Defines environment variable names used by the Data Plane CLI.
/// </summary>
internal static class EnvironmentVariables
{
    /// <summary>
    /// Encryption key used to decrypt sensitive values
    /// in the Data Plane Operator configuration file.
    ///
    /// </summary>
    public const string OperatorEncryptionKey =
        "CN_DP_OPERATOR_ENCRYPTION_KEY";
}