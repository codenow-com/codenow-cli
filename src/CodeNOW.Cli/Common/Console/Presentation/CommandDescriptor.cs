namespace CodeNOW.Cli.Common.Console.Presentation;

/// <summary>
/// Describes a CLI command and its presentation behavior.
/// </summary>
public sealed record CommandDescriptor(string Module, string Command, bool HideBanner);
