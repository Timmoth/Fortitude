using Fortitude.Example.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Fortitude.Client;

public static class HttpClientExtensions
{
    public static IServiceCollection AddFortitudeClient(this IServiceCollection services, FortitudeClient client)
    {
        services.AddSingleton(client);
        services.AddSingleton<HttpClientInterceptorMiddleware>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, InterceptionFilter>(p => new InterceptionFilter(p));
        return services;
    }
}

public class InterceptionFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _provider;

    internal InterceptionFilter(IServiceProvider provider)
        => _provider = provider;
    
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        => builder =>
        {
            var handler = _provider.GetRequiredService<HttpClientInterceptorMiddleware>();
            next(builder);
            builder.AdditionalHandlers.Add(handler);
        };
}

public class HttpClientInterceptorMiddleware(FortitudeClient client) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await client.TryHandle(request, cancellationToken);
    }
}