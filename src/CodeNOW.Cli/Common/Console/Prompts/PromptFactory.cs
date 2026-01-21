using Spectre.Console;

namespace CodeNOW.Cli.Common.Console.Prompts;

/// <summary>
/// Factory for building console prompts with optional prefill.
/// </summary>
public sealed class PromptFactory
{
    private readonly bool _usePrefill;

    /// <summary>
    /// Creates a new prompt factory.
    /// </summary>
    /// <param name="usePrefill">Whether prompts should prefill values for edit flows.</param>
    public PromptFactory(bool usePrefill)
    {
        _usePrefill = usePrefill;
    }

    /// <summary>
    /// Creates a string prompt with optional default and validation.
    /// </summary>
    /// <param name="prompt">Prompt text displayed to the user.</param>
    /// <param name="initialValue">Initial input value for prefilled prompts.</param>
    /// <param name="defaultValue">Default value used when the input is empty.</param>
    /// <param name="showDefaultValue">Whether to render the default value.</param>
    /// <param name="allowEmpty">Whether empty input is allowed.</param>
    /// <param name="secret">Whether input should be masked.</param>
    /// <param name="validator">Optional validator for the input.</param>
    /// <returns>A configured prompt instance.</returns>
    public IPrompt<string> CreateStringPrompt(
        string prompt,
        string? initialValue = null,
        string? defaultValue = null,
        bool showDefaultValue = true,
        bool allowEmpty = false,
        bool secret = false,
        Func<string, ValidationResult>? validator = null)
    {
        if (_usePrefill)
        {
            var prefilled = new PrefilledTextPrompt<string>(prompt);
            if (initialValue is not null)
                prefilled.WithInitialValue(initialValue);
            if (defaultValue is not null)
                prefilled.DefaultValue(defaultValue);
            prefilled.ShowDefaultValue = showDefaultValue;
            prefilled.AllowEmpty = allowEmpty;
            prefilled.IsSecret = secret;
            if (validator is not null)
                prefilled.Validator = validator;
            return prefilled;
        }

        var promptText = new TextPrompt<string>(prompt);
        if (defaultValue is not null)
            promptText.DefaultValue(defaultValue);
        promptText.ShowDefaultValue = showDefaultValue;
        promptText.AllowEmpty = allowEmpty;
        promptText.IsSecret = secret;
        if (validator is not null)
            promptText.Validator = validator;
        return promptText;
    }

    /// <summary>
    /// Creates an integer prompt with optional default and validation.
    /// </summary>
    /// <param name="prompt">Prompt text displayed to the user.</param>
    /// <param name="initialValue">Initial input value for prefilled prompts.</param>
    /// <param name="defaultValue">Default value used when the input is empty.</param>
    /// <param name="showDefaultValue">Whether to render the default value.</param>
    /// <returns>A configured prompt instance.</returns>
    public IPrompt<int> CreateIntPrompt(
        string prompt,
        int? initialValue = null,
        int? defaultValue = null,
        bool showDefaultValue = true)
    {
        if (_usePrefill)
        {
            var prefilled = new PrefilledTextPrompt<int>(prompt);
            if (initialValue.HasValue)
                prefilled.WithInitialValue(initialValue.Value);
            if (defaultValue.HasValue)
                prefilled.DefaultValue(defaultValue.Value);
            prefilled.ShowDefaultValue = showDefaultValue;
            return prefilled;
        }

        var promptText = new TextPrompt<int>(prompt);
        if (defaultValue.HasValue)
            promptText.DefaultValue(defaultValue.Value);
        promptText.ShowDefaultValue = showDefaultValue;
        return promptText;
    }

    /// <summary>
    /// Creates a boolean prompt with optional default.
    /// </summary>
    /// <param name="prompt">Prompt text displayed to the user.</param>
    /// <param name="initialValue">Initial input value for prefilled prompts.</param>
    /// <param name="defaultValue">Default value used when the input is empty.</param>
    /// <param name="showDefaultValue">Whether to render the default value.</param>
    /// <param name="showChoices">Whether to show y/n choices.</param>
    /// <returns>A configured prompt instance.</returns>
    public IPrompt<bool> CreateBoolPrompt(
        string prompt,
        bool? initialValue = null,
        bool? defaultValue = null,
        bool showDefaultValue = true,
        bool showChoices = true)
    {
        if (_usePrefill)
        {
            var prefilled = new PrefilledTextPrompt<bool>(prompt);
            if (initialValue.HasValue)
                prefilled.WithInitialValue(initialValue.Value);
            if (defaultValue.HasValue)
                prefilled.DefaultValue(defaultValue.Value);
            prefilled.ShowDefaultValue = showDefaultValue;
            prefilled.ShowChoices = showChoices;
            prefilled.Choices.Add(true);
            prefilled.Choices.Add(false);
            prefilled.Converter = choice => choice ? "y" : "n";
            return prefilled;
        }

        var promptText = new TextPrompt<bool>(prompt);
        if (defaultValue.HasValue)
            promptText.DefaultValue(defaultValue.Value);
        promptText.ShowDefaultValue = showDefaultValue;
        promptText.ShowChoices = showChoices;
        promptText.Choices.Add(true);
        promptText.Choices.Add(false);
        promptText.Converter = choice => choice ? "y" : "n";
        return promptText;
    }

    /// <summary>
    /// Builds a choice list with the existing value first when present.
    /// </summary>
    /// <param name="existingValue">Existing value that should appear first.</param>
    /// <param name="choices">Available choices.</param>
    /// <typeparam name="T">Enum type of the choices.</typeparam>
    /// <returns>Ordered list of choices.</returns>
    public static IReadOnlyList<T> BuildSelectionChoices<T>(T? existingValue, params T[] choices)
        where T : struct, Enum
    {
        var ordered = new List<T>();
        if (existingValue.HasValue)
            ordered.Add(existingValue.Value);
        foreach (var choice in choices)
        {
            if (!ordered.Contains(choice))
                ordered.Add(choice);
        }
        return ordered;
    }
}
