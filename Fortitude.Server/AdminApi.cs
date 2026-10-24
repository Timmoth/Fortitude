using Fortitude.Client;
using Microsoft.AspNetCore.Mvc;

namespace Fortitude.Server;

/// <summary>
///     A small HTTP API over the server's handler definitions and observed traffic.
/// </summary>
/// <remarks>
///     Handlers could previously only be configured from .NET or by mounting YAML files, which
///     made the server awkward to drive from a JS, Python or Java test suite, or from a CI
///     pipeline in any other language. Everything the live UI does is available here.
///     <para>
///         The API is unauthenticated, like the UI. Turn it off with
///         <c>Settings:EnableAdminApi=false</c> when the server is reachable by anything you
///         do not trust.
///     </para>
/// </remarks>
public static class AdminApi
{
    /// <summary>Mounts the admin API under <c>/fortitude/api</c>.</summary>
    public static IEndpointRouteBuilder MapFortitudeAdminApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/fortitude/api");

        MapHandlerEndpoints(api);
        MapTrafficEndpoints(api);
        MapClientEndpoints(api);

        return app;
    }

    private static void MapHandlerEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/handlers", (YamlHandlerStore store) =>
            Results.Ok(new HandlerListResponse(
                store.Directory,
                store.Files.Select(f => new HandlerFileResponse(
                    f.FileName,
                    f.Handlers.Count,
                    f.Error)).ToList())));

        api.MapGet("/handlers/{fileName}", (string fileName, YamlHandlerStore store) =>
        {
            try
            {
                var path = store.ResolveFilePath(fileName);

                return File.Exists(path)
                    ? Results.Text(File.ReadAllText(path), "application/yaml")
                    : Results.NotFound(new ErrorResponse($"No handler file named '{fileName}'."));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        api.MapPut("/handlers/{fileName}", async (
            string fileName,
            HttpRequest request,
            YamlHandlerStore store) =>
        {
            using var reader = new StreamReader(request.Body);
            var content = await reader.ReadToEndAsync();

            try
            {
                store.Save(fileName, content);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }

            // Report a parse failure rather than silently accepting a file that defines
            // nothing; the caller has no other way to find out.
            var saved = store.Files.FirstOrDefault(f => f.FileName == fileName);

            if (saved is { HasError: true })
                return Results.UnprocessableEntity(
                    new ErrorResponse($"Saved, but the file could not be parsed: {saved.Error}"));

            return Results.Ok(new HandlerFileResponse(fileName, saved?.Handlers.Count ?? 0, null));
        });

        api.MapDelete("/handlers/{fileName}", (string fileName, YamlHandlerStore store) =>
        {
            try
            {
                store.Delete(fileName);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        api.MapPost("/handlers/reload", (YamlHandlerStore store) =>
        {
            store.Reload();
            return Results.Ok(new HandlerListResponse(
                store.Directory,
                store.Files.Select(f => new HandlerFileResponse(
                    f.FileName, f.Handlers.Count, f.Error)).ToList()));
        });
    }

    private static void MapTrafficEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/requests", ([FromQuery] int? limit, RequestTracker tracker) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, RequestTracker.Capacity);

            var recent = tracker.Requests
                .Reverse()
                .Take(take)
                .Select(r => new RecordedRequest(
                    r.RequestId,
                    r.Time,
                    r.Method,
                    r.Route,
                    r.RawQuery,
                    r.Headers,
                    r.Body == null ? null : System.Text.Encoding.UTF8.GetString(r.Body),
                    tracker.GetResponse(r.RequestId) is { } response
                        ? new RecordedResponse(response.Status, response.ContentType)
                        : null))
                .ToList();

            return Results.Ok(new RequestListResponse(tracker.TotalRequests, tracker.RetainedRequests, recent));
        });

        api.MapDelete("/requests", (RequestTracker tracker) =>
        {
            tracker.Clear();
            return Results.NoContent();
        });
    }

    private static void MapClientEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/clients", (ConnectedClientService clients) =>
            Results.Ok(new ClientListResponse(
                clients.Count,
                clients.Clients.Select(c => new ConnectedClientResponse(c.Key, c.Value)).ToList())));
    }

    // ---------------------------------------------------------------- contracts

    /// <summary>The handler definition files currently loaded.</summary>
    public sealed record HandlerListResponse(string Directory, IReadOnlyList<HandlerFileResponse> Files);

    /// <summary>One handler definition file.</summary>
    public sealed record HandlerFileResponse(string FileName, int Handlers, string? Error);

    /// <summary>Recent traffic, newest first.</summary>
    public sealed record RequestListResponse(long Total, int Retained, IReadOnlyList<RecordedRequest> Requests);

    /// <summary>One observed request and the response it received.</summary>
    public sealed record RecordedRequest(
        Guid Id,
        DateTimeOffset Time,
        string Method,
        string Route,
        string Query,
        IReadOnlyDictionary<string, string[]> Headers,
        string? Body,
        RecordedResponse? Response);

    /// <summary>The response to an observed request.</summary>
    public sealed record RecordedResponse(int Status, string ContentType);

    /// <summary>Clients currently connected over SignalR.</summary>
    public sealed record ClientListResponse(int Count, IReadOnlyList<ConnectedClientResponse> Clients);

    /// <summary>One connected client and the port it reserved, or -1 if unrouted.</summary>
    public sealed record ConnectedClientResponse(string ConnectionId, int Port);

    /// <summary>A failure, with a message intended to be read by a human.</summary>
    public sealed record ErrorResponse(string Message);
}
