using Fortitude.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Fortitude.Server;

/// <summary>
///     Middleware that forwards HTTP requests to the Fortitude client associated with the port
///     that the HTTP request arrived on. It then waits for a response and forwards it back.
/// </summary>
public class FortitudeMiddleware(
    RequestDelegate next,
    PendingRequestStore pending,
    IHubContext<FortitudeHub> hub,
    ILogger<FortitudeMiddleware> logger,
    RequestTracker tracker,
    ConnectedClientService connectedClientService,
    IOptions<Settings> settings,
    YamlHandlerStore yamlHandlers)

{
    private readonly ConnectedClientService _connectedClientService =
        connectedClientService ?? throw new ArgumentNullException(nameof(connectedClientService));

    private readonly IHubContext<FortitudeHub> _hub = hub ?? throw new ArgumentNullException(nameof(hub));
    private readonly ILogger<FortitudeMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly PendingRequestStore _pending = pending ?? throw new ArgumentNullException(nameof(pending));
    private readonly Settings _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
    private readonly RequestTracker _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));

        // Ignore internal routes
        if (ctx.Request.Path.StartsWithSegments("/fortitude", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        // Determine request target port
        var requestPort = ctx.Connection.LocalPort;

        _logger.LogInformation(
            "Incoming HTTP request {Method} {Path} on port {Port}",
            ctx.Request.Method,
            ctx.Request.Path,
            requestPort);


        // Create FortitudeRequest
        var requestId = Guid.NewGuid();
        FortitudeRequest req;
        
        try
        {
            req = await FortitudeRequest.FromHttpContext(ctx, requestId).ConfigureAwait(false);
            _tracker.Add(req);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse request {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Failed to create request.").ConfigureAwait(false);
            return;
        }

        var response = await yamlHandlers.HandleIncomingAsync(req);
        if (response != null)
        {
            _tracker.Add(response);
            await WriteResponse(ctx, response).ConfigureAwait(false);

            _logger.LogInformation("Response sent for request {RequestId}", requestId);
            return;
        }
        
        // A client that reserved this port owns it exclusively, which is what keeps
        // concurrent tests isolated. Only when nobody owns the port do we fall back to the
        // clients holding no reservation — the in-memory TestServer case, a single-port
        // deployment where reservation was unavailable, or an explicit Settings:Broadcast.
        var targetClientId = _connectedClientService.GetClientByPort(requestPort);
        var recipients = targetClientId != null
            ? [targetClientId]
            : _connectedClientService.UnroutedClients;

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "No connected client is bound to port {Port}. Cannot route request.",
                requestPort);

            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync(
                    $"No Fortitude client is connected for port {requestPort}.")
                .ConfigureAwait(false);
            return;
        }

        if (targetClientId != null)
            _logger.LogInformation(
                "Routing request on port {Port} to client {ClientId}",
                requestPort, targetClientId);
        else
            _logger.LogInformation(
                "No client owns port {Port}; delivering to {Count} unrouted client(s)",
                requestPort, recipients.Count);

        try
        {
            await _hub.Clients.Clients(recipients)
                .SendAsync("IncomingRequest", req)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send request {RequestId} to client(s)", requestId);

            var error = new FortitudeResponse(requestId).InternalServerError();
            _tracker.Add(error);
            await WriteResponse(ctx, error).ConfigureAwait(false);
            return;
        }

        // Await response
        try
        {
            response = await _pending.WaitForResponse(
                requestId,
                _settings.ResponseTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timed out after {Timeout} waiting for a response to {RequestId}. "
                + "Raise Settings:ResponseTimeoutSeconds if the handler needs longer.",
                _settings.ResponseTimeout, requestId);
            response = new FortitudeResponse(requestId).GatewayTimeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for response {RequestId}", requestId);
            response = new FortitudeResponse(requestId).InternalServerError();
        }

        _tracker.Add(response);
        await WriteResponse(ctx, response).ConfigureAwait(false);

        _logger.LogInformation("Response sent for request {RequestId}", requestId);
    }

    /// <summary>
    ///     Writes a FortitudeResponse to HTTP output.
    /// </summary>
    private static async Task WriteResponse(HttpContext ctx, FortitudeResponse response)
    {
        // A simulated reset has to happen at the connection level: there is no status code
        // that represents "the server vanished".
        if (response.Fault == HttpFault.ConnectionReset)
        {
            ctx.Abort();
            return;
        }

        ctx.Response.StatusCode = response.Status;

        // Applies even to an empty body: a 204 or 304 may still need to declare its type.
        // Written before the headers so an explicit Content-Type header, being the more
        // specific instruction, takes precedence over the response's ContentType property.
        if (!string.IsNullOrEmpty(response.ContentType))
            ctx.Response.ContentType = response.ContentType;

        // The handler is the authority on what the mocked service returns, so its headers
        // replace anything already set. Previously they were skipped if the key existed,
        // which made Content-Type and other server-set headers impossible to override.
        foreach (var header in response.Headers)
        {
            // Kestrel derives these from the body; setting them by hand corrupts the response.
            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            ctx.Response.Headers[header.Key] = header.Value;
        }

        if (response.Body is { Length: > 0 })
            await ctx.Response.Body.WriteAsync(response.Body).ConfigureAwait(false);
    }
}