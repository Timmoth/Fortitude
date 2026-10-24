using System.Text.Json;
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
    private readonly object _handlersLock = new();
    private readonly ILogger<FortitudeClient> _logger;

    /// <summary>
    ///     Immutable snapshot of the handlers, swapped on every registration.
    ///     Request dispatch reads this so it never iterates a list being mutated.
    /// </summary>
    private volatile FortitudeHandler[] _handlerSnapshot = [];

    /// <summary>Requests that no registered handler matched.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<FortitudeRequest> _unmatched = new();
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
    ///     Requests that arrived but matched no handler, and were answered with
    ///     501 Not Implemented.
    /// </summary>
    /// <remarks>
    ///     An unmatched request usually means the system under test called something the test
    ///     did not set up, or that a matcher is subtly wrong. Both are easy to miss, because
    ///     a 501 often just looks like the dependency misbehaving. See
    ///     <see cref="VerifyNoUnmatchedRequests" />.
    /// </remarks>
    public IReadOnlyList<FortitudeRequest> UnmatchedRequests => _unmatched.ToArray();

    /// <summary>
    ///     Throws if any request went unmatched, listing what arrived and what was registered.
    /// </summary>
    /// <exception cref="FortitudeVerificationException">Thrown when there are unmatched requests.</exception>
    public void VerifyNoUnmatchedRequests()
    {
        var unmatched = UnmatchedRequests;

        if (unmatched.Count == 0)
            return;

        var message = new System.Text.StringBuilder();

        message.AppendLine(unmatched.Count == 1
            ? "1 request was not matched by any handler:"
            : $"{unmatched.Count} requests were not matched by any handler:");

        foreach (var request in unmatched.Take(10))
            message.AppendLine($"    {request.Method} {request.Route}{request.RawQuery}");

        if (unmatched.Count > 10)
            message.AppendLine($"    ... and {unmatched.Count - 10} more");

        var handlers = _handlerSnapshot;

        if (handlers.Length == 0)
        {
            message.AppendLine("  No handlers were registered.");
        }
        else
        {
            message.AppendLine("  Registered handlers:");
            foreach (var handler in handlers)
                message.AppendLine($"    {handler}{(handler.IsExhausted ? "  (exhausted)" : string.Empty)}");
        }

        throw new FortitudeVerificationException(message.ToString().TrimEnd());
    }

    /// <summary>Forgets any unmatched requests recorded so far.</summary>
    public void ClearUnmatchedRequests()
    {
        _unmatched.Clear();
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

        lock (_handlersLock)
        {
            if (_handlers.Contains(handler))
            {
                _logger.LogInformation("[Duplicate Handler]: {HandlerName} ", handler.ToString());
                return;
            }

            _handlers.Add(handler);
            _handlerSnapshot = _handlers.ToArray();
        }

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
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Server URL cannot be null or empty.", nameof(url));

        _logger.LogInformation("Connecting to Fortitude server at {Url}...", url);

        var connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        return await StartAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Starts the Fortitude client using an already configured <see cref="HubConnection" />.
    /// </summary>
    /// <param name="connection">The hub connection to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The port assigned by the server, or -1 if none was assigned.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the client is already started.</exception>
    public async Task<int> StartAsync(HubConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        if (_connection != null)
            throw new InvalidOperationException("FortitudeClient is already started.");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        connection.On<FortitudeRequest>("IncomingRequest", async req =>
        {
            try
            {
                await HandleIncomingAsync(req);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request {RequestId}", req.RequestId);

                await connection.InvokeAsync("SubmitResponse",
                    new FortitudeResponse(req.RequestId).InternalServerError(ex.Message), cancellationToken);
            }
        });

        try
        {
            await connection.StartAsync(cts.Token).ConfigureAwait(false);
            _logger.LogInformation("Connected to Fortitude server.");
        }
        catch (Exception ex)
        {
            // Leave the client in its pre-start state so the caller can retry; otherwise the
            // next attempt fails with "already started" instead of reporting the real problem.
            cts.Dispose();
            _logger.LogError(ex, "Failed to connect to Fortitude server.");
            throw;
        }

        _connection = connection;
        _cts = cts;

        try
        {
            Port = await connection.InvokeAsync<int>("GetAssignedPort", cancellationToken).ConfigureAwait(false);

            if (Port > 0)
                _logger.LogInformation("This client has been assigned port {Port}", Port);
            else
                _logger.LogWarning(
                    "This client has NOT been assigned a port and will receive unrouted traffic. "
                    + "Start the server with PORTS=<range> for per-client isolation.");
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

        var handler = FortitudeHandler.SelectFor(_handlerSnapshot, request);

        if (handler != null)
        {
            var response = await handler.HandleRequestAsync(request);

            _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
            _logger.LogInformation("[Handled] {response}", response);

            await _connection.InvokeAsync("SubmitResponse", response);
            return;
        }

        _unmatched.Enqueue(request);

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

        var handler = FortitudeHandler.SelectFor(_handlerSnapshot, req);

        if (handler != null)
        {
            var response = await handler.HandleRequestAsync(req);

            _logger.LogInformation("[Incoming]: {RequestId}", request.ToString());
            _logger.LogInformation("[Handled] {response}", response);

            return response.ToHttpResponseMessage();
        }

        _unmatched.Enqueue(req);

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

        return Create(logger.WriteLine);
    }

    /// <summary>
    ///     Creates a <see cref="FortitudeClient" /> that writes diagnostics to a callback.
    /// </summary>
    /// <param name="writeLine">
    ///     Receives one line per log entry — <c>TestContext.WriteLine</c> under NUnit or
    ///     MSTest, or any other sink.
    /// </param>
    public static FortitudeClient Create(Action<string> writeLine)
    {
        if (writeLine is null)
            throw new ArgumentNullException(nameof(writeLine));

        return new FortitudeClient(new TestOutputLogger<FortitudeClient>(writeLine));
    }

    /// <summary>
    ///     Creates a new instance of <see cref="FortitudeClient" /> for testing purposes
    /// </summary>
    /// <returns>A new <see cref="FortitudeClient" /> instance.</returns>
    public static FortitudeClient Create()
    {
        var logger = LoggerFactory.Create(builder => builder.AddSimpleConsole()).CreateLogger<FortitudeClient>();
        return new FortitudeClient(logger);
    }
}