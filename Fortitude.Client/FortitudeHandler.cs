namespace Fortitude.Client;

public static class FortitudeHandler
{
    public static FortitudeHandlerBuilder Accepts(this FortitudeClient client)
    {
        var handler = new FortitudeHandlerBuilder(client);
        return handler;
    }
    

public class FortitudeHandlerBuilder
{
    public FortitudeHandlerBuilder(FortitudeClient fortitudeClient, string? method = null)
    {
        client = fortitudeClient;
        if (!string.IsNullOrEmpty(method))
        {
            Method(method);
        }
    }
    private readonly FortitudeClient client;
    private HashSet<string> _methods = new();
    private string? _route;
    private Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _queryParams = new(StringComparer.OrdinalIgnoreCase);
    private Func<byte[]?, bool>? _bodyPredicate;

    public FortitudeHandlerBuilder Method(string method)
    {
        _methods.Add(method.ToUpperInvariant());
        return this;
    }

    public FortitudeHandlerBuilder Get() => Method("GET");
    public FortitudeHandlerBuilder Post() => Method("POST");
    public FortitudeHandlerBuilder Put() => Method("PUT");
    public FortitudeHandlerBuilder Delete() => Method("DELETE");
    public FortitudeHandlerBuilder Patch() => Method("PATCH");
    public FortitudeHandlerBuilder Options() => Method("OPTIONS");

    public FortitudeHandlerBuilder Methods(params string[] methods)
    {
        foreach (var m in methods)
            _methods.Add(m.ToUpperInvariant());
        return this;
    }

    public FortitudeHandlerBuilder HttpRoute(string route)
    {
        _route = route;
        return this;
    }

    public FortitudeHandlerBuilder Header(string key, string value)
    {
        _headers[key] = value;
        return this;
    }
    
    public FortitudeHandlerBuilder QueryParam(string key, string value)
    {
        _queryParams[key] = value;
        return this;
    }
    
    public FortitudeHandlerBuilder Body(Func<byte[]?, bool> predicate)
    {
        _bodyPredicate = predicate;
        return this;
    }
    
    public FortitudeHandlerBase Returns(Action<FortitudeRequest, FortitudeResponse> responder)
    {
        var handler = new LambdaFortitudeHandlerBase(
            _methods,
            _route,
            _headers,
            _queryParams,
            _bodyPredicate,
            responder);
        client.Add(handler);
        return handler;
    }

    public FortitudeHandlerBase Returns(Func<FortitudeRequest, FortitudeResponse, Task> asyncResponder)
    {
        var handler = new LambdaFortitudeHandlerBase(
            _methods,
            _route,
            _headers,
            _queryParams,
            _bodyPredicate,
            asyncResponder);
        client.Add(handler);
        return handler;
    }
}

}