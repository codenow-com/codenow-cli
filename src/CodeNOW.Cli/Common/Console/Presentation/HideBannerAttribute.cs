namespace CodeNOW.Cli.Common.Console.Presentation;

/// <summary>
/// Marks a command or command container as one that should not show the banner.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
public sealed class HideBannerAttribute : Attribute
{
}
