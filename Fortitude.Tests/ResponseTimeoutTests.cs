using System.Diagnostics;
using System.Net;
using Fortitude.Client;
using Fortitude.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Covers Settings:ResponseTimeoutSeconds — how long the server waits for a connected client
///     to produce a response before returning 504.
/// </summary>
public class ResponseTimeoutTests(ITestOutputHelper output)
{
    [Fact]
    public void DefaultTimeout_IsFiveSeconds()
    {
        Assert.Equal(5, new Settings().ResponseTimeoutSeconds);
        Assert.Equal(TimeSpan.FromSeconds(5), new Settings().ResponseTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeout_FallsBackToTheDefault(int configured)
    {
        var settings = new Settings { ResponseTimeoutSeconds = configured };

        Assert.Equal(TimeSpan.FromSeconds(Settings.DefaultResponseTimeoutSeconds), settings.ResponseTimeout);
    }

    [Fact]
    public void ConfiguredTimeout_IsHonoured()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), new Settings { ResponseTimeoutSeconds = 42 }.ResponseTimeout);
    }

    [Fact]
    public void TheDoubleUnderscoreEnvironmentVariableForm_Binds()
    {
        // Settings__ResponseTimeoutSeconds is the form the README tells Docker users to pass,
        // so pin it rather than take it on trust.
        const string key = "Settings__ResponseTimeoutSeconds";

        Environment.SetEnvironmentVariable(key, "30");
        try
        {
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            var settings = new Settings();
            config.GetSection("Settings").Bind(settings);

            Assert.Equal(30, settings.ResponseTimeoutSeconds);
            Assert.Equal(TimeSpan.FromSeconds(30), settings.ResponseTimeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public async Task AHandlerSlowerThanTheConfiguredTimeout_Returns504()
    {
        // 1s timeout against a handler that takes far longer. If the setting were ignored the
        // hard-coded 5s would apply, so the elapsed time is what proves it was read.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Settings:ResponseTimeoutSeconds"] = "1"
                    })));

        var client = factory.CreateClient();

        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/fortitude/hub",
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();

        var fortitude = FortitudeClient.Create(output);
        await fortitude.StartAsync(hubConnection);

        fortitude.Accepts()
            .Get()
            .HttpRoute("/slow")
            .Returns(async (_, res) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                res.Ok("too late");
            });

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/slow");
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Expected the 1s configured timeout to apply, but the request took {stopwatch.Elapsed}. "
            + "That suggests the previous hard-coded 5s is still in effect.");

        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task AHandlerInsideTheTimeout_StillSucceeds()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Settings:ResponseTimeoutSeconds"] = "10"
                    })));

        var client = factory.CreateClient();

        var hubConnection = new HubConnectionBuilder()
            .WithUrl("http://localhost/fortitude/hub",
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler())
            .Build();

        var fortitude = FortitudeClient.Create(output);
        await fortitude.StartAsync(hubConnection);

        fortitude.Accepts()
            .Get()
            .HttpRoute("/brief")
            .Returns(async (_, res) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200));
                res.Ok("in time");
            });

        var response = await client.GetAsync("/brief");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("in time", await response.Content.ReadAsStringAsync());

        await hubConnection.DisposeAsync();
    }
}
