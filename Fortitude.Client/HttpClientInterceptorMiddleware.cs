using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Fortitude.Client;

public static class HttpClientExtensions
{
    public static IServiceCollection AddFortitudeClient(this IServiceCollection services, FortitudeClient client)
    {
        services.AddSingleton(client);
        services.AddTransient<HttpClientInterceptorMiddleware>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, InterceptionFilter>(p => new InterceptionFilter(p));
        return services;
    }
}

public class InterceptionFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _provider;

    public InterceptionFilter(IServiceProvider provider)
    {
        _provider = provider;
    }

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

public class HttpClientInterceptorMiddleware(FortitudeClient client) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await client.TryHandle(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}