using Spectre.Console;

namespace CodeNOW.Cli.Common.Console.Prompts;

/// <summary>
/// Common input validators for console prompts.
/// </summary>
public static class PromptValidators
{
    /// <summary>
    /// Validates that the value is a non-empty absolute URL.
    /// </summary>
    /// <param name="value">User input.</param>
    /// <returns>Validation result for the URL.</returns>
    public static ValidationResult ValidateUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("[red]URL is required.[/]");

        return Uri.TryCreate(value, UriKind.Absolute, out _)
            ? ValidationResult.Success()
            : ValidationResult.Error("[red]Enter a valid absolute URL.[/]");
    }

    /// <summary>
    /// Validates that the value starts with the IAM role ARN prefix.
    /// </summary>
    /// <param name="value">User input.</param>
    /// <returns>Validation result for the IAM role.</returns>
    public static ValidationResult ValidateIamRoleArnPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("[red]IAM role is required.[/]");

        return value.StartsWith("arn:aws:iam::", StringComparison.Ordinal)
            ? ValidationResult.Success()
            : ValidationResult.Error("[red]IAM role must start with \"arn:aws:iam::\".[/]");
    }
}
