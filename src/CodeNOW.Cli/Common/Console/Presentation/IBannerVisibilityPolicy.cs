namespace CodeNOW.Cli.Common.Console.Presentation;

/// <summary>
/// Evaluates whether the banner and related hints should be displayed.
/// </summary>
public interface IBannerVisibilityPolicy
{
    /// <summary>
    /// Determines whether the banner should be displayed.
    /// </summary>
    bool ShouldShowBanner(string[] args);

    /// <summary>
    /// Determines whether the awaiting instruction hint should be displayed.
    /// </summary>
    bool ShouldShowAwaitingInstruction(string[] args);
}
