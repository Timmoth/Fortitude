using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Fortitude.Client;

/// <summary>
///     A client that connects to a Fortitude server and processes incoming requests using registered handlers.
/// </summary>
public class FortitudeClient : IAsyncDisposable
{
    private readonly List<FortitudeHandler> _handlers = new();
    private readonly ILogger<FortitudeClient> _logger;
    private HubConnection? _connection;
    private CancellationTokenSource? _cts;

    /// <summary>
    ///     Initializes a new instance of <see cref="FortitudeClient" />.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger" /> is null.</exception>
    public FortitudeClient(ILogger<FortitudeClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the port assigned to this client by the Fortitude server.
    ///     The value is -1 until the client has successfully connected and been assigned a port.
    /// </summary>
    public int Port { get; private set; } = -1;

    /// <summary>
    ///     Disposes the Fortitude client asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    /// <summary>
    ///     Adds a request handler to the client.
    /// </summary>
    /// <param name="handler">The handler to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="handler" /> is null.</exception>
    public void AddHandler(FortitudeHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _handlers.Add(handler);
        _logger.LogInformation("Registered Handler: {HandlerName}", handler.ToString());
    }

    /// <summary>
    ///     Starts the Fortitude client and connects to the specified server URL.
    /// </summary>
    /// <param name="url">The URL of the Fortitude server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the client is already started.</exception>
    public async Task<int> StartAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_connection != null)
            throw new InvalidOperationException("FortitudeClient is already started.");

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Server URL cannot be null or empty.", nameof(url));

        _logger.LogInformation("Connecting to Fortitude server at {Url}...", url);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<FortitudeRequest>("IncomingRequest", async req =>
        {
            try
            {
                await HandleIncomingAsync(req);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request {RequestId}", req.RequestId);
                if (_connection != null)
                    await _connection.InvokeAsync("SubmitResponse",
                        new FortitudeResponse(req.RequestId).InternalServerError(ex.Message), cancellationToken);
            }
        });

        try
        {
            await _connection.StartAsync(_cts.Token);
            _logger.LogInformation("Connected to Fortitude server.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Fortitude server at {Url}", url);
            throw;
        }

        try
        {
            Port = await _connection.InvokeAsync<int>("GetAssignedPort", cancellationToken);

            if (Port > 0)
                _logger.LogInformation("This client has been assigned port {Port}", Port);
            else
                _logger.LogWarning("This client has NOT been assigned a port.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query assigned port from server.");
        }

        return Port;
    }

    /// <summary>
    ///     Stops the Fortitude client and disconnects from the server.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _logger.LogInformation("Stopping Fortitude Client...");

        try
        {
            _cts.Cancel();

            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping Fortitude client.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _logger.LogInformation("Fortitude Client stopped.");
        }
    }

    /// <summary>
    ///     Processes an incoming request using the registered handlers.
    /// </summary>
    /// <param name="request">The incoming Fortitude request.</param>
    private async Task HandleIncomingAsync(FortitudeRequest request)
    {
        if (_connection == null)
            return;

        for (var i = _handlers.Count - 1; i >= 0; i--)
        {
            var handler = _handlers[i];
            if (!handler.Matches(request)) continue;

            var response = await handler.HandleRequestAsync(request);

            _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
            _logger.LogInformation("[Handled] {response}", response);

            await _connection.InvokeAsync("SubmitResponse", response);
            return;
        }

        var defaultResponse = new FortitudeResponse(request.RequestId).MethodNotImplemented();
        _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
        _logger.LogWarning("[Ignored] {defaultResponse}.", defaultResponse);
        await _connection.InvokeAsync("SubmitResponse", defaultResponse);
    }

    /// <summary>
    ///     Attempts to handle an <see cref="HttpRequestMessage" /> using the registered handlers.
    ///     This method is intended for internal use by interceptors.
    /// </summary>
    /// <param name="request">The <see cref="HttpRequestMessage" /> to handle.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="HttpResponseMessage" /> representing the response from the handler.</returns>
    public async Task<HttpResponseMessage> TryHandle(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();

        var req = await FortitudeRequest.FromHttpRequestMessage(request, requestId).ConfigureAwait(false);

        for (var i = _handlers.Count - 1; i >= 0; i--)
        {
            var handler = _handlers[i];
            if (!handler.Matches(req)) continue;

            var response = await handler.HandleRequestAsync(req);

            _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
            _logger.LogInformation("[Handled] {response}", response);

            return response.ToHttpResponseMessage();
        }

        var defaultResponse = new FortitudeResponse(requestId).MethodNotImplemented();
        _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
        _logger.LogWarning("[Ignored] {defaultResponse}.", defaultResponse);
        return defaultResponse.ToHttpResponseMessage();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="FortitudeClient" /> for testing purposes,
    ///     using an xUnit <see cref="ITestOutputHelper" /> for logging.
    /// </summary>
    /// <param name="logger">The xUnit test output helper.</param>
    /// <returns>A new <see cref="FortitudeClient" /> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger" /> is null.</exception>
    public static FortitudeClient Create(
        ITestOutputHelper logger)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        return new FortitudeClient(new TestOutputLogger<FortitudeClient>(logger));
    }
}