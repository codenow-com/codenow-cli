using CodeNOW.Cli.DataPlane.Services.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class KubernetesReadExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesAndReturnsResult()
    {
        var executor = new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>());
        var attempts = 0;

        var result = await executor.ExecuteAsync(
            () =>
            {
                attempts++;
                if (attempts < 2)
                    throw new Exception("boom");
                return Task.FromResult("ok");
            },
            warningMessage: "failed",
            fallback: "fallback",
            maxAttempts: 2);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFallbackOnFailure()
    {
        var executor = new KubernetesReadExecutor(new NullLogger<KubernetesReadExecutor>());

        var result = await executor.ExecuteAsync(
            () => Task.FromException<string>(new InvalidOperationException("boom")),
            warningMessage: "failed",
            fallback: "fallback",
            maxAttempts: 1);

        Assert.Equal("fallback", result);
    }
}
