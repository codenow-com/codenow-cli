namespace CodeNOW.Cli.Common.Console.Presentation;

/// <summary>
/// Determines when to show the CLI banner based on the invoked command.
/// </summary>
public sealed class BannerVisibilityPolicy : IBannerVisibilityPolicy
{
    private readonly IReadOnlyList<CommandDescriptor> commands;

    public BannerVisibilityPolicy(IReadOnlyList<CommandDescriptor> commands)
    {
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <inheritdoc />
    public bool ShouldShowBanner(string[] args)
    {
        // Always show the banner on help output to avoid a blank header.
        if (args.Any(token => token is "-h" or "--help"))
            return true;

        var invocation = ParseInvocation(args);
        if (invocation is null)
            return true;

        var match = commands.FirstOrDefault(cmd =>
            cmd.Module.Equals(invocation.Value.Module, StringComparison.OrdinalIgnoreCase) &&
            cmd.Command.Equals(invocation.Value.Command, StringComparison.OrdinalIgnoreCase));

        return match is null || !match.HideBanner;
    }

    /// <inheritdoc />
    public bool ShouldShowAwaitingInstruction(string[] args)
    {
        var invocation = ParseInvocation(args);
        if (invocation is null)
            return true;

        return invocation.Value.Command is null &&
               invocation.Value.Module.Equals("dp", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Module, string? Command)? ParseInvocation(string[] args)
    {
        if (args.Length == 0)
            return null;

        var tokens = args
            .Where(token => !string.IsNullOrWhiteSpace(token) && !token.StartsWith("-", StringComparison.Ordinal))
            .ToArray();

        if (tokens.Length == 0)
            return null;

        var module = tokens[0];
        var command = tokens.Length > 1 ? tokens[1] : null;
        return (module, command);
    }
}
