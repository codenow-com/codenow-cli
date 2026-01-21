using CodeNOW.Cli.Common.Console.Prompts;
using Spectre.Console;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Console.Prompts;

public class PromptFactoryTests
{
    private enum SampleChoice
    {
        First,
        Second,
        Third
    }

    [Fact]
    public void BuildSelectionChoices_IncludesExistingFirst()
    {
        var ordered = PromptFactory.BuildSelectionChoices(SampleChoice.Second, SampleChoice.First, SampleChoice.Second, SampleChoice.Third);

        Assert.Equal(SampleChoice.Second, ordered[0]);
        Assert.Equal(3, ordered.Count);
    }

    [Fact]
    public void CreateStringPrompt_UsesPrefilledPromptWhenEnabled()
    {
        var factory = new PromptFactory(usePrefill: true);

        var prompt = factory.CreateStringPrompt("Name:");

        Assert.IsType<PrefilledTextPrompt<string>>(prompt);
    }

    [Fact]
    public void CreateStringPrompt_UsesTextPromptWhenPrefillDisabled()
    {
        var factory = new PromptFactory(usePrefill: false);

        var prompt = factory.CreateStringPrompt("Name:");

        Assert.IsType<TextPrompt<string>>(prompt);
    }

    [Fact]
    public void CreateBoolPrompt_ConfiguresChoices()
    {
        var factory = new PromptFactory(usePrefill: false);

        var prompt = factory.CreateBoolPrompt("Enabled:");

        var textPrompt = Assert.IsType<TextPrompt<bool>>(prompt);
        Assert.Contains(true, textPrompt.Choices);
        Assert.Contains(false, textPrompt.Choices);
    }

    [Fact]
    public void CreateBoolPrompt_UsesPrefilledPromptWhenEnabled()
    {
        var factory = new PromptFactory(usePrefill: true);

        var prompt = factory.CreateBoolPrompt("Enabled:");

        var prefilled = Assert.IsType<PrefilledTextPrompt<bool>>(prompt);
        Assert.Contains(true, prefilled.Choices);
        Assert.Contains(false, prefilled.Choices);
    }
}
