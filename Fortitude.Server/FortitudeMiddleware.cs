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
        /// <summary>
        /// Initializes a new instance of <see cref="FortitudeMiddleware"/>.
        /// </summary>
        public FortitudeMiddleware(
            RequestDelegate next,
            PendingRequestStore pending,
            IHubContext<FortitudeHub> hub,
            ILogger<FortitudeMiddleware> logger, RequestTracker tracker)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tracker = tracker;
        }

        /// <summary>
        /// Invokes the middleware for the given <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="ctx">The HTTP context.</param>
        public async Task InvokeAsync(HttpContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            _logger.LogInformation("Middleware invoked for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

            // Pass through /fortitude endpoint requests to next middleware
            if (ctx.Request.Path.StartsWithSegments("/fortitude", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Passing through {Path} to next middleware", ctx.Request.Path);
                await _next(ctx).ConfigureAwait(false);
                return;
            }

            // Build FortitudeRequest from HttpContext
            var requestId = Guid.NewGuid();
            FortitudeRequest req;
            try
            {
                req = await FortitudeRequest.FromHttpContext(ctx, requestId).ConfigureAwait(false);
                _logger.LogInformation("Created request {RequestId} {Method} {Route}", requestId, req.Method, req.Route);
                _tracker.Add(req);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create FortitudeRequest for {Path}", ctx.Request.Path);
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Failed to create request.").ConfigureAwait(false);
                return;
            }

            if (FortitudeHub.ConnectedClients.IsEmpty)
            {
                _logger.LogWarning("No connected clients. The request may timeout.");
            }
            else
            {
                _logger.LogInformation("Connected clients: {Count}", FortitudeHub.ConnectedClients.Count);
            }

            // Send request to all connected SignalR clients
            try
            {
                _logger.LogInformation("Sending IncomingRequest {RequestId} to SignalR clients", requestId);
                await _hub.Clients.All.SendAsync("IncomingRequest", req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send IncomingRequest {RequestId}", requestId);
                _tracker.Add(new FortitudeResponse(req.RequestId).InternalServerError());
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Failed to send request to clients.").ConfigureAwait(false);
                return;
            }

            // Wait for a response from clients
            FortitudeResponse response;
            try
            {
                _logger.LogInformation("Waiting for response for {RequestId}", requestId);
                response = await _pending.WaitForResponse(requestId, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                _logger.LogInformation("Received response for {RequestId} with status {Status}", requestId, response.Status);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout waiting for response for {RequestId}", requestId);
                _tracker.Add(new FortitudeResponse(req.RequestId).GatewayTimeout());

                ctx.Response.StatusCode = 504; // Gateway Timeout
                await ctx.Response.WriteAsync("No response from client in time.").ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for response for {RequestId}", requestId);
                _tracker.Add(new FortitudeResponse(req.RequestId).InternalServerError());

                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Error while waiting for response.").ConfigureAwait(false);
                return;
            }
            
            _tracker.Add(response);

            // Write response to HTTP pipeline
            ctx.Response.StatusCode = response.Status;
            foreach (var header in response.Headers)
            {
                ctx.Response.Headers[header.Key] = header.Value;
            }

            if (!string.IsNullOrEmpty(response.Body))
            {
                await ctx.Response.WriteAsync(response.Body).ConfigureAwait(false);
            }

            _logger.LogInformation("Response sent for {RequestId}", requestId);
        }
    }
}
