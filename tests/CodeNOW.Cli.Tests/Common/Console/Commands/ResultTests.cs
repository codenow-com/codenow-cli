using CodeNOW.Cli.Common.Console.Commands;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Console.Commands;

public class ResultTests
{
    [Fact]
    public void Ok_ReturnsSuccessWithZeroExitCode()
    {
        var result = Result.Ok();

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_ReturnsFailureWithMessageAndExitCode()
    {
        var result = Result.Fail("boom", 7);

        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("boom", result.Error);
    }
}
