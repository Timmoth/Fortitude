using Microsoft.AspNetCore.SignalR.Client;
using Xunit.Abstractions;

namespace Fortitude.Client;

public class FortitudeClient(ITestOutputHelper logger) : IAsyncDisposable
{
    private readonly ITestOutputHelper _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private HubConnection? _connection;
    private readonly List<FortitudeHandlerBase> _handlers = [];
    private CancellationTokenSource? _cts;

    public void Add(FortitudeHandlerBase handlerBase)
    {
        _handlers.Add(handlerBase);
        _logger.WriteLine($"Handler added: {handlerBase.GetType().Name}");
    }
    
    public async Task StartAsync(string url)
    {
        _logger.WriteLine($"Connecting to Fortitude server at {url}...");

        _cts = new CancellationTokenSource();

        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<FortitudeRequest>("IncomingRequest", async req =>
        {
            _logger.WriteLine($"Incoming Fortitude request: {req.RequestId} {req.Method} {req.Route}");
            await HandleIncoming(req);
        });

        await _connection.StartAsync(_cts.Token);
        _logger.WriteLine("Connected to Fortitude server.");
    }
    
    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _logger.WriteLine("Stopping Fortitude Client...");
        await _cts.CancelAsync();

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _cts.Dispose();
        _cts = null;

        _logger.WriteLine("Fortitude Client stopped.");
    }

    private async Task HandleIncoming(FortitudeRequest req)
    {
        if (_connection == null)
            return;

        foreach (var handler in _handlers.Where(handler => handler.Matches(req)))
        {
            _logger.WriteLine($"Handler matched: {handler.GetType().Name} for request {req.RequestId}");
            var response = await handler.BuildResponse(req);
            await _connection.InvokeAsync("SubmitResponse", response);
            return;
        }

        _logger.WriteLine($"No handler matched request {req.RequestId}. Returning NotFound.");
        await _connection.InvokeAsync("SubmitResponse", FortitudeResponse.NotFound(req.RequestId));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}