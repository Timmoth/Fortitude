using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Fortitude.Client;

/// <summary>
///     Registers Fortitude as an in-process interceptor for outgoing <see cref="HttpClient" />
///     calls, so a system under test can be mocked without an external server.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    ///     Routes every <see cref="HttpClient" /> created by the DI container through
    ///     <paramref name="client" />'s handlers. Requests that match never leave the process;
    ///     requests that do not are answered with 501.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="client">The configured Fortitude client holding the handlers.</param>
    public static IServiceCollection AddFortitudeClient(this IServiceCollection services, FortitudeClient client)
    {
        services.AddSingleton(client);
        services.AddTransient<HttpClientInterceptorMiddleware>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, InterceptionFilter>(p => new InterceptionFilter(p));
        return services;
    }
}

/// <summary>
///     Appends <see cref="HttpClientInterceptorMiddleware" /> to every named and typed
///     <see cref="HttpClient" /> the container builds.
/// </summary>
public class InterceptionFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _provider;

    /// <summary>Initializes a new instance of <see cref="InterceptionFilter" />.</summary>
    /// <param name="provider">Provider used to construct the interceptor.</param>
    public InterceptionFilter(IServiceProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);
            var handler = ActivatorUtilities.CreateInstance<HttpClientInterceptorMiddleware>(_provider);
            builder.AdditionalHandlers.Add(handler);
        };
    }
}

/// <summary>
///     A <see cref="DelegatingHandler" /> that answers outgoing requests from the Fortitude
///     client's handlers instead of sending them.
/// </summary>
/// <param name="client">The client whose handlers answer the request.</param>
public class HttpClientInterceptorMiddleware(FortitudeClient client) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await client.TryHandle(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}