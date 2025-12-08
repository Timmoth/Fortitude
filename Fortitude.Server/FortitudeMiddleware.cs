using Fortitude.Client;
using Microsoft.AspNetCore.SignalR;

namespace Fortitude.Server
{
    /// <summary>
    /// Middleware that forwards HTTP requests to connected Fortitude clients via SignalR,
    /// waits for a response, and returns it to the original HTTP caller.
    /// </summary>
    public class FortitudeMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly PendingRequestStore _pending;
        private readonly IHubContext<FortitudeHub> _hub;
        private readonly ILogger<FortitudeMiddleware> _logger;
        private readonly RequestTracker _tracker;
        private readonly ConnectedClientService _connectedClientService;

        public FortitudeMiddleware(
            RequestDelegate next,
            PendingRequestStore pending,
            IHubContext<FortitudeHub> hub,
            ILogger<FortitudeMiddleware> logger,
            RequestTracker tracker,
            ConnectedClientService connectedClientService)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _connectedClientService = connectedClientService ?? throw new ArgumentNullException(nameof(connectedClientService));
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            
            if (ctx.Request.Path.StartsWithSegments("/fortitude", StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx).ConfigureAwait(false);
                return;
            }

            _logger.LogInformation("Middleware invoked for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

            var requestId = Guid.NewGuid();
            FortitudeRequest req;
            try
            {
                req = await FortitudeRequest.FromHttpContext(ctx, requestId).ConfigureAwait(false);
                _tracker.Add(req);
                _logger.LogInformation("Created request {RequestId} {Method} {Route}", requestId, req.Method, req.Route);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create FortitudeRequest for {Path}", ctx.Request.Path);
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Failed to create request.").ConfigureAwait(false);
                return;
            }

            if (_connectedClientService.Clients.IsEmpty)
            {
                _logger.LogWarning("No connected clients. The request may timeout.");
            }

            try
            {
                await _hub.Clients.All.SendAsync("IncomingRequest", req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send IncomingRequest {RequestId}", requestId);
                var errorResponse = new FortitudeResponse(req.RequestId).InternalServerError();
                _tracker.Add(errorResponse);
                await WriteResponse(ctx, errorResponse).ConfigureAwait(false);
                return;
            }

            FortitudeResponse response;
            try
            {
                response = await _pending.WaitForResponse(requestId, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout waiting for response for {RequestId}", requestId);
                response = new FortitudeResponse(req.RequestId).GatewayTimeout();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for response for {RequestId}", requestId);
                response = new FortitudeResponse(req.RequestId).InternalServerError();
            }

            _tracker.Add(response);

            await WriteResponse(ctx, response).ConfigureAwait(false);

            _logger.LogInformation("Response sent for {RequestId}", requestId);
        }

        /// <summary>
        /// Writes the FortitudeResponse to the HTTP context.
        /// Uses Status, Headers, ContentType, and Body.
        /// </summary>
        private static async Task WriteResponse(HttpContext ctx, FortitudeResponse response)
        {
            ctx.Response.StatusCode = response.Status;

            // Write headers (do not overwrite essential headers like Content-Length)
            foreach (var header in response.Headers)
            {
                if (!ctx.Response.Headers.ContainsKey(header.Key))
                    ctx.Response.Headers[header.Key] = header.Value;
            }

            // Write body if present
            if (response.Body != null && response.Body.Length > 0)
            {
                ctx.Response.ContentType = response.ContentType ?? "application/octet-stream";
                await ctx.Response.Body.WriteAsync(response.Body, 0, response.Body.Length).ConfigureAwait(false);
            }
        }
    }
}
