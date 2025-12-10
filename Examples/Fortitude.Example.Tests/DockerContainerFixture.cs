namespace Fortitude.Example.Api;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Xunit; 
public sealed class DockerContainerFixture : IAsyncLifetime
{
    private readonly IFutureDockerImage _image;
    public IContainer Container { get; }
    public string Host { get; private set; } = default!;
    public int MappedPort { get; private set; }

    public DockerContainerFixture()
    {
        _image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
            .WithDockerfile("Dockerfile")
            .WithName("fortitude-server-test:latest") 
            .Build();

        Container = new ContainerBuilder()
            .WithImage(_image)
            .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _image.CreateAsync().ConfigureAwait(false);
        await Container.StartAsync().ConfigureAwait(false);
        
        Host = Container.Hostname;
        MappedPort = Container.GetMappedPublicPort(8080);
    }

    public Task DisposeAsync()
    {
        return Container.DisposeAsync().AsTask();
    }
}