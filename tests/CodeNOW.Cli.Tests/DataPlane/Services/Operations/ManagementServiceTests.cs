using CodeNOW.Cli.Adapters.Kubernetes;
using CodeNOW.Cli.DataPlane.Services.Operations;
using CodeNOW.Cli.Tests.TestDoubles.Kubernetes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class ManagementServiceTests
{
    [Fact]
    public async Task GetClusterVersionAsync_UsesClientVersion()
    {
        var client = new FakeKubernetesClient();
        client.Version.GetCodeAsyncHandler = _ => Task.FromResult(new k8s.Models.VersionInfo { GitVersion = "v1.26.0" });
        var factory = new FakeKubernetesClientFactory(client);

        var service = new ManagementService(new NullLogger<ManagementService>(), factory, new KubernetesConnectionOptions());

        var version = await service.GetClusterVersionAsync();

        Assert.Equal("v1.26.0", version);
    }

    private sealed class FakeKubernetesClientFactory : IKubernetesClientFactory
    {
        private readonly IKubernetesClient client;

        public FakeKubernetesClientFactory(IKubernetesClient client)
        {
            this.client = client;
        }

        public IKubernetesClient Create(Microsoft.Extensions.Logging.ILogger logger, KubernetesConnectionOptions options) => client;
    }
}
