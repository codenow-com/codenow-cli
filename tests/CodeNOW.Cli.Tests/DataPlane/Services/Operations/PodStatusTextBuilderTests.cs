using CodeNOW.Cli.DataPlane.Services.Operations;
using k8s.Models;
using Xunit;

namespace CodeNOW.Cli.Tests.DataPlane.Services.Operations;

public class PodStatusTextBuilderTests
{
    [Fact]
    public void BuildPodStatusText_ReturnsTerminatingForDeletedPods()
    {
        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                DeletionTimestamp = DateTime.UtcNow
            }
        };

        var status = PodStatusTextBuilder.BuildPodStatusText(pod);

        Assert.Equal("Terminating", status);
    }

    [Fact]
    public void BuildPodStatusText_ReturnsInitStatus()
    {
        var pod = new V1Pod
        {
            Spec = new V1PodSpec
            {
                InitContainers = [new V1Container { Name = "init" }]
            },
            Status = new V1PodStatus
            {
                InitContainerStatuses =
                [
                    new V1ContainerStatus
                    {
                        State = new V1ContainerState
                        {
                            Waiting = new V1ContainerStateWaiting { Reason = "Pulling" }
                        }
                    }
                ]
            }
        };

        var status = PodStatusTextBuilder.BuildPodStatusText(pod);

        Assert.Equal("Init:Pulling", status);
    }

    [Fact]
    public void BuildPodStatusText_ReturnsCompletedForSucceeded()
    {
        var pod = new V1Pod
        {
            Status = new V1PodStatus { Phase = "Succeeded" }
        };

        var status = PodStatusTextBuilder.BuildPodStatusText(pod);

        Assert.Equal("Completed", status);
    }
}
