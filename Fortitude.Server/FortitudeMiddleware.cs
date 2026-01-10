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
    HandlerSet handlers)

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

        var response = await handlers.HandleIncomingAsync(req);
        if (response != null)
        {
            _tracker.Add(response);
            await WriteResponse(ctx, response).ConfigureAwait(false);

            _logger.LogInformation("Response sent for request {RequestId}", requestId);
            return;
        }
        
        if (_settings.Broadcast)
        {
            _logger.LogInformation(
                "Broadcast request on port {Port}",
                requestPort);

            // Send only to the target client
            try
            {
                await _hub.Clients.All
                    .SendAsync("IncomingRequest", req)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast request {RequestId}",
                    requestId);

                var error = new FortitudeResponse(requestId).InternalServerError();
                _tracker.Add(error);
                await WriteResponse(ctx, error).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            // Determine which client handles this port
            var targetClientId = _connectedClientService.GetClientByPort(requestPort);

            if (targetClientId == null)
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

            _logger.LogInformation(
                "Routing request on port {Port} to client {ClientId}",
                requestPort,
                targetClientId);

            // Send only to the target client
            try
            {
                await _hub.Clients.Client(targetClientId)
                    .SendAsync("IncomingRequest", req)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send request {RequestId} to client {ClientId}",
                    requestId, targetClientId);

                var error = new FortitudeResponse(requestId).InternalServerError();
                _tracker.Add(error);
                await WriteResponse(ctx, error).ConfigureAwait(false);
                return;
            }
        }

        // Await response
        try
        {
            response = await _pending.WaitForResponse(
                requestId,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timeout waiting for response for {RequestId}", requestId);
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
        ctx.Response.StatusCode = response.Status;

        foreach (var header in response.Headers)
            if (!ctx.Response.Headers.ContainsKey(header.Key))
                ctx.Response.Headers[header.Key] = header.Value;

        if (response.Body is { Length: > 0 })
        {
            ctx.Response.ContentType = response.ContentType ?? "application/octet-stream";
            await ctx.Response.Body.WriteAsync(response.Body, 0, response.Body.Length)
                .ConfigureAwait(false);
        }
    }
}