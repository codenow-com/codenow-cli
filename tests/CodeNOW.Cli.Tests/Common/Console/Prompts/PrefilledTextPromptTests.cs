using CodeNOW.Cli.Common.Console.Prompts;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Console.Prompts;

public class PrefilledTextPromptTests
{
    [Fact]
    public void Constructor_ThrowsOnNullPrompt()
    {
        Assert.Throws<ArgumentNullException>(() => new PrefilledTextPrompt<string>(null!));
    }

    [Fact]
    public void WithInitialValue_ReturnsSameInstance()
    {
        var prompt = new PrefilledTextPrompt<string>("Value:");

        var returned = prompt.WithInitialValue("demo");

        Assert.Same(prompt, returned);
    }

    [Fact]
    public void DefaultValue_ReturnsSameInstance()
    {
        var prompt = new PrefilledTextPrompt<int>("Number:");

        var returned = prompt.DefaultValue(5);

        Assert.Same(prompt, returned);
    }
}
