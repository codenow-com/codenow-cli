using System.Globalization;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console;

namespace CodeNOW.Cli.Common.Console.Prompts;

/// <summary>
/// Text prompt that can prefill user input while preserving Spectre.Console defaults and validation.
/// </summary>
public sealed class PrefilledTextPrompt<T> : IPrompt<T>, IHasCulture
{
    private readonly string _prompt;
    private readonly StringComparer? _comparer;
    private bool _hasInitialValue;
    private T? _initialValue;
    private bool _hasDefaultValue;
    private T? _defaultValue;

    /// <summary>
    /// Style applied to the prompt text.
    /// </summary>
    public Style? PromptStyle { get; set; }
    /// <summary>
    /// Available choices for the prompt.
    /// </summary>
    public List<T> Choices { get; } = new();
    /// <summary>
    /// Culture used for formatting and parsing.
    /// </summary>
    public CultureInfo? Culture { get; set; }
    /// <summary>
    /// Message displayed when the user selects an invalid choice.
    /// </summary>
    public string InvalidChoiceMessage { get; set; } = "[red]Please select one of the available options[/]";
    /// <summary>
    /// Whether input should be treated as secret.
    /// </summary>
    public bool IsSecret { get; set; }
    /// <summary>
    /// Mask character used for secret input.
    /// </summary>
    public char? Mask { get; set; } = '*';
    /// <summary>
    /// Message displayed when validation fails.
    /// </summary>
    public string ValidationErrorMessage { get; set; } = "[red]Invalid input[/]";
    /// <summary>
    /// Whether to display choices.
    /// </summary>
    public bool ShowChoices { get; set; } = true;
    /// <summary>
    /// Whether to display the default value.
    /// </summary>
    public bool ShowDefaultValue { get; set; } = true;
    /// <summary>
    /// Whether empty input is allowed.
    /// </summary>
    public bool AllowEmpty { get; set; }
    /// <summary>
    /// Converter used to display values.
    /// </summary>
    public Func<T, string>? Converter { get; set; } = ConvertToString;
    /// <summary>
    /// Validator for user input.
    /// </summary>
    public Func<T, ValidationResult>? Validator { get; set; }
    /// <summary>
    /// Style applied to the default value display.
    /// </summary>
    public Style? DefaultValueStyle { get; set; }
    /// <summary>
    /// Style applied to choice list display.
    /// </summary>
    public Style? ChoicesStyle { get; set; }

    /// <summary>
    /// Initializes a new prompt instance.
    /// </summary>
    /// <param name="prompt">Prompt markup.</param>
    /// <param name="comparer">Comparer for choice input.</param>
    public PrefilledTextPrompt(string prompt, StringComparer? comparer = null)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _comparer = comparer;
    }

    /// <summary>
    /// Sets the initial value shown in the prompt.
    /// </summary>
    /// <param name="value">Initial value to prefill.</param>
    /// <returns>The current prompt instance.</returns>
    public PrefilledTextPrompt<T> WithInitialValue(T value)
    {
        _hasInitialValue = true;
        _initialValue = value;
        return this;
    }

    /// <summary>
    /// Sets the default value when the user provides no input.
    /// </summary>
    /// <param name="value">Default value used when input is empty.</param>
    /// <returns>The current prompt instance.</returns>
    public PrefilledTextPrompt<T> DefaultValue(T value)
    {
        _hasDefaultValue = true;
        _defaultValue = value;
        return this;
    }

    /// <summary>
    /// Displays the prompt and returns the entered value.
    /// </summary>
    /// <param name="console">Console to display the prompt.</param>
    /// <returns>Entered value.</returns>
    public T Show(IAnsiConsole console)
    {
        return ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Displays the prompt asynchronously and returns the entered value.
    /// </summary>
    /// <param name="console">Console to display the prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entered value.</returns>
    public async Task<T> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        if (console is null)
        {
            throw new ArgumentNullException(nameof(console));
        }

        return await console.RunExclusive(async () =>
        {
            var promptStyle = PromptStyle ?? Style.Plain;
            var converter = Converter ?? ConvertToString;
            var choices = Choices.Select(choice => converter(choice)).ToList();
            var choiceMap = Choices.ToDictionary(choice => converter(choice), choice => choice, _comparer);

            var (promptMarkup, promptLength) = BuildPromptMarkup(converter);

            bool useInitialValue = _hasInitialValue;

            while (true)
            {
                var initialInput = useInitialValue ? converter(_initialValue!) : null;
                useInitialValue = false;

                var (input, usedInitialSecret) = ReadLine(
                    console,
                    promptMarkup,
                    promptLength,
                    initialInput,
                    IsSecret,
                    _hasInitialValue ? converter(_initialValue!) : null,
                    Mask,
                    cancellationToken);

                if (usedInitialSecret)
                {
                    console.WriteLine();
                    return _initialValue!;
                }

                // Nothing entered?
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (_hasDefaultValue)
                    {
                        var defaultValue = converter(_defaultValue!);
                        console.Write(IsSecret ? defaultValue.Mask(Mask) : defaultValue, promptStyle);
                        console.WriteLine();
                        return _defaultValue!;
                    }

                    if (!AllowEmpty)
                    {
                        continue;
                    }
                }

                console.WriteLine();

                T? result;
                if (Choices.Count > 0)
                {
                    if (choiceMap.TryGetValue(input, out result) && result != null)
                    {
                        return result;
                    }
                    else
                    {
                        console.MarkupLine(InvalidChoiceMessage);
                        continue;
                    }
                }
                else if (!TryConvert(input, Culture, out result) || result == null)
                {
                    console.MarkupLine(ValidationErrorMessage);
                    continue;
                }

                // Run all validators
                if (!ValidateResult(result, out var validationMessage))
                {
                    console.MarkupLine(validationMessage);
                    continue;
                }

                return result;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the prompt markup and returns its plain-text length for cursor placement.
    /// </summary>
    private (string Markup, int Length) BuildPromptMarkup(Func<T, string> converter)
    {
        var builder = new StringBuilder();
        builder.Append(_prompt.TrimEnd());

        var appendSuffix = false;
        if (ShowChoices && Choices.Count > 0)
        {
            appendSuffix = true;
            var choices = string.Join("/", Choices.Select(choice => converter(choice)));
            var choicesStyle = ChoicesStyle?.ToMarkup() ?? "blue";
            builder.AppendFormat(CultureInfo.InvariantCulture, " [{0}][[{1}]][/]", choicesStyle, choices);
        }

        if (ShowDefaultValue && _hasDefaultValue)
        {
            appendSuffix = true;
            var defaultValueStyle = DefaultValueStyle?.ToMarkup() ?? "green";
            var defaultValue = converter(_defaultValue!);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                " [{0}]({1})[/]",
                defaultValueStyle,
                IsSecret ? defaultValue.Mask(Mask) : defaultValue);
        }

        var markup = builder.ToString().Trim();
        if (appendSuffix)
        {
            markup += ":";
        }

        markup += " ";

        var plain = Markup.Remove(markup);
        return (markup, plain.Length);
    }

    /// <summary>
    /// Reads a line while keeping the prompt rendered and cursor positioned correctly.
    /// </summary>
    private static (string Input, bool UsedInitialSecret) ReadLine(
        IAnsiConsole console,
        string promptMarkup,
        int promptLength,
        string? initialInput,
        bool isSecret,
        string? initialSecretValue,
        char? mask,
        CancellationToken cancellationToken)
    {
        var hasSecretPrefill = isSecret && !string.IsNullOrEmpty(initialSecretValue);
        var input = new StringBuilder(hasSecretPrefill ? "*" : (initialInput ?? string.Empty));
        var cursorIndex = input.Length;
        var secretModified = false;
        var promptRow = System.Console.CursorTop;

        RenderLine(console, promptMarkup, promptLength, input.ToString(), cursorIndex, isSecret, mask, promptRow);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = System.Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return (input.ToString(), hasSecretPrefill && !secretModified);
            }

            switch (key.Key)
            {
                case ConsoleKey.Backspace:
                    if (cursorIndex > 0)
                    {
                        input.Remove(cursorIndex - 1, 1);
                        cursorIndex--;
                        if (hasSecretPrefill)
                            secretModified = true;
                    }
                    break;
                case ConsoleKey.Delete:
                    if (cursorIndex < input.Length)
                    {
                        input.Remove(cursorIndex, 1);
                        if (hasSecretPrefill)
                            secretModified = true;
                    }
                    break;
                case ConsoleKey.LeftArrow:
                    if (cursorIndex > 0)
                        cursorIndex--;
                    break;
                case ConsoleKey.RightArrow:
                    if (cursorIndex < input.Length)
                        cursorIndex++;
                    break;
                case ConsoleKey.Home:
                    cursorIndex = 0;
                    break;
                case ConsoleKey.End:
                    cursorIndex = input.Length;
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        if (hasSecretPrefill && !secretModified)
                        {
                            input.Clear();
                            input.Append(key.KeyChar);
                            cursorIndex = 1;
                            secretModified = true;
                        }
                        else
                        {
                            input.Insert(cursorIndex, key.KeyChar);
                            cursorIndex++;
                        }
                    }
                    break;
            }

            RenderLine(console, promptMarkup, promptLength, input.ToString(), cursorIndex, isSecret, mask, promptRow);
        }
    }

    /// <summary>
    /// Renders the current input line and positions the cursor.
    /// </summary>
    private static void RenderLine(
        IAnsiConsole console,
        string promptMarkup,
        int promptLength,
        string input,
        int cursorIndex,
        bool isSecret,
        char? mask,
        int promptRow)
    {
        var inputDisplay = isSecret ? new string(mask ?? '*', input.Length) : input;

        System.Console.SetCursorPosition(0, promptRow);
        System.Console.Write("\u001b[2K");

        var markup = promptMarkup + Markup.Escape(inputDisplay);
        console.Markup(markup);

        System.Console.SetCursorPosition(promptLength + cursorIndex, promptRow);
    }

    /// <summary>
    /// Validates the parsed result using the configured validator.
    /// </summary>
    private bool ValidateResult(T value, [NotNullWhen(false)] out string? message)
    {
        if (Validator != null)
        {
            var result = Validator(value);
            if (!result.Successful)
            {
                message = result.Message ?? ValidationErrorMessage;
                return false;
            }
        }

        message = null;
        return true;
    }

    /// <summary>
    /// Converts a value to a display string.
    /// </summary>
    private static string ConvertToString(T value)
    {
        if (value is null)
            return string.Empty;

        return value switch
        {
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Attempts to parse input into the target type.
    /// </summary>
    private static bool TryConvert(string input, CultureInfo? culture, out T? result)
    {
        result = default;
        if (typeof(T) == typeof(string))
        {
            result = (T)(object)input;
            return true;
        }

        if (typeof(T) == typeof(int))
        {
            if (int.TryParse(input, NumberStyles.Integer, culture ?? CultureInfo.CurrentCulture, out var parsed))
            {
                result = (T)(object)parsed;
                return true;
            }
            return false;
        }

        if (typeof(T) == typeof(bool))
        {
            if (bool.TryParse(input, out var parsed))
            {
                result = (T)(object)parsed;
                return true;
            }
            return false;
        }

        return false;
    }
}
