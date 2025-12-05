using Fortitude.Client;
using Microsoft.AspNetCore.SignalR;

namespace Fortitude.Server;

public class FortitudeMiddleware(
    RequestDelegate next,
    PendingRequestStore pending,
    IHubContext<FortitudeHub> hub,
    ILogger<FortitudeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        logger.LogInformation("Middleware invoked for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        // Pass through all /fortitude requests
        if (ctx.Request.Path.StartsWithSegments("/fortitude", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Passing through {Path} to next middleware", ctx.Request.Path);
            await next(ctx);
            return;
        }

        // Build request from HttpContext
        var requestId = Guid.NewGuid();
        FortitudeRequest req;
        try
        {
            req = await FortitudeRequest.FromHttpContext(ctx, requestId);
            logger.LogInformation("Created Request {RequestId} {Method} {Route}", requestId, req.Method, req.Route);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create request for {Path}", ctx.Request.Path);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Failed to create request.");
            return;
        }
        logger.LogInformation("Connected clients: {Count}", FortitudeHub.ConnectedClients.Count);

        if (FortitudeHub.ConnectedClients.IsEmpty)
        {
            logger.LogWarning("No clients are connected! The request may timeout.");
        }
        else
        {
            logger.LogInformation("Connected clients: {Count}", FortitudeHub.ConnectedClients.Count);
        }
        
        // Push request to all connected SignalR clients
        try
        {
            logger.LogInformation("Sending IncomingRequest {RequestId} to SignalR clients", requestId);
            await hub.Clients.All.SendAsync("IncomingRequest", req);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send IncomingRequest {RequestId}", requestId);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Failed to send request to client.");
            return;
        }

        // Wait for response from client
        FortitudeResponse response;
        try
        {
            logger.LogInformation("Waiting for response for {RequestId}", requestId);
            response = await pending.WaitForResponse(requestId, TimeSpan.FromSeconds(30));
            logger.LogInformation("Received response for {RequestId} with status {Status}", requestId, response.Status);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Timeout waiting for response for {RequestId}", requestId);
            ctx.Response.StatusCode = 504; // Gateway Timeout
            await ctx.Response.WriteAsync("No response from client in time.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error waiting for response for {RequestId}", requestId);
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Error while waiting for response.");
            return;
        }

        // Write the response to HTTP pipeline
        ctx.Response.StatusCode = response.Status;
        foreach (var header in response.Headers)
        {
            ctx.Response.Headers[header.Key] = header.Value;
        }

        if (!string.IsNullOrEmpty(response.Body))
        {
            await ctx.Response.WriteAsync(response.Body);
        }

        logger.LogInformation("Response sent for {RequestId}", requestId);
    }
}