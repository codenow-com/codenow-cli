using CodeNOW.Cli.Common.Console.Prompts;
using Spectre.Console;

namespace CodeNOW.Cli.DataPlane.Console.Prompts;

/// <summary>
/// Shared namespace prompt definitions used by the bootstrap wizard and permissions command.
/// </summary>
internal static class NamespacePrompts
{
    public static string PromptSystemNamespace(PromptFactory? promptFactory = null, string? initialValue = null) =>
        Prompt("System namespace", DataPlaneConstants.DefaultSystemNamespace, promptFactory, initialValue);

    public static string PromptCniNamespace(PromptFactory? promptFactory = null, string? initialValue = null) =>
        Prompt("CNI namespace", DataPlaneConstants.DefaultCniNamespace, promptFactory, initialValue);

    public static string PromptCiPipelinesNamespace(PromptFactory? promptFactory = null, string? initialValue = null) =>
        Prompt("CI Pipelines namespace", DataPlaneConstants.DefaultCiPipelinesNamespace, promptFactory, initialValue);

    private static string Prompt(string label, string defaultValue, PromptFactory? promptFactory, string? initialValue)
    {
        if (promptFactory is not null)
        {
            return AnsiConsole.Prompt(promptFactory.CreateStringPrompt(
                $"{label} [green]({defaultValue})[/]:",
                initialValue: initialValue,
                defaultValue: defaultValue,
                showDefaultValue: false));
        }

        return AnsiConsole.Prompt(
            new TextPrompt<string>($"{label} [green]({defaultValue})[/]:")
                .DefaultValue(defaultValue)
                .ShowDefaultValue(false));
    }
}

