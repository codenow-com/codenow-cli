using CodeNOW.Cli.Common.Console.Prompts;
using Spectre.Console;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Console.Prompts;

public class PromptValidatorsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidateUrl_RejectsEmpty(string value)
    {
        var result = PromptValidators.ValidateUrl(value);

        Assert.False(result.Successful);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("example.com")]
    public void ValidateUrl_RejectsInvalidUrl(string value)
    {
        var result = PromptValidators.ValidateUrl(value);

        Assert.False(result.Successful);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:8080/path")]
    public void ValidateUrl_AcceptsAbsoluteUrl(string value)
    {
        var result = PromptValidators.ValidateUrl(value);

        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidateIamRoleArnPrefix_RejectsEmpty(string value)
    {
        var result = PromptValidators.ValidateIamRoleArnPrefix(value);

        Assert.False(result.Successful);
    }

    [Theory]
    [InlineData("arn:aws:iam::123456789012:role/MyRole")]
    [InlineData("arn:aws:iam::000000000000:role/role-name")]
    public void ValidateIamRoleArnPrefix_AcceptsValidPrefix(string value)
    {
        var result = PromptValidators.ValidateIamRoleArnPrefix(value);

        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData("arn:aws:iam:role/Nope")]
    [InlineData("arn:aws:sts::123456789012:assumed-role/role")]
    public void ValidateIamRoleArnPrefix_RejectsInvalidPrefix(string value)
    {
        var result = PromptValidators.ValidateIamRoleArnPrefix(value);

        Assert.False(result.Successful);
    }
}
