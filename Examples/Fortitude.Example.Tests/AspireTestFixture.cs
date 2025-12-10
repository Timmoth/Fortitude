using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Projects;

namespace Fortitude.Example.Api;

public sealed class AspireTestFixture : IAsyncLifetime
{
    public DistributedApplication? _app;
    private ResourceNotificationService? _notificationService;

    public async Task InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Fortitude_Example_AppHost>();
        
        _app = await builder.BuildAsync();

        _notificationService = _app.Services.GetService<ResourceNotificationService>();

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }
    
    public async Task<HttpClient> CreateHttpClient(string applicationName)
    {
        var _testingClient = _app!.CreateHttpClient(applicationName);
        
        await _notificationService!.WaitForResourceAsync(applicationName, KnownResourceStates.Running).WaitAsync(TimeSpan.FromSeconds(30));

        return _testingClient;
    }
}