namespace Fortitude.Client;

public static class FortitudeHandler
{
    public static FortitudeHandlerBuilder For(this FortitudeClient client)
    {
        var handler = new FortitudeHandlerBuilder(client);
        return handler;
    }

public class FortitudeHandlerBuilder(FortitudeClient fortitudeClient)
{
    private readonly FortitudeClient client = fortitudeClient;
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
    
    public FortitudeHandlerBase Build(Func<FortitudeRequest, FortitudeResponse> responder)
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

    public FortitudeHandlerBase Build(Func<FortitudeRequest, Task<FortitudeResponse>> asyncResponder)
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