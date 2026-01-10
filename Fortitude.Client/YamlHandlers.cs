using System.Text;
using System.Text.Json;
using Jint;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fortitude.Client;

public static class FortitudeYamlLoader
{
    private static IEnumerable<string> LoadYamlFiles(string dir)
    {
        if (!Directory.Exists(dir)) yield break;

        foreach (var file in Directory.EnumerateFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            yield return text;
        }
    }
    
    public static IEnumerable<FortitudeHandler> LoadHandlers(string dir)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        
        foreach (var yaml in LoadYamlFiles(dir))
        {
            var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml).Handlers;
            foreach (var handlerDef in handlerDefs)
            {
                yield return ToHandler(handlerDef);
            }
        }
    }
    
    public static FortitudeHandler FromYamlSingle(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var handler = deserializer.Deserialize<HandlerDefinition>(yaml);
        return ToHandler(handler);
    }
    
    public static IEnumerable<FortitudeHandler> FromYamlMulti(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml).Handlers;
        foreach (var handlerDef in handlerDefs)
        {
            yield return ToHandler(handlerDef);
        }
    }

    private static object ConvertNumber(object value)
    {
        if (value is string s && int.TryParse(s, out var i))
            return i;
        if (value is string s2 && double.TryParse(s2, out var d))
            return d;
        return value;
    }

    private static Dictionary<string, object> CoerceNumbers(Dictionary<string, object> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value switch
        {
            Dictionary<string, object> d => CoerceNumbers(d),
            List<object> l => l.Select(ConvertNumber).ToList(),
            _ => ConvertNumber(kvp.Value)
        });
    }
    
    private static bool EvaluateJsonPredicate(byte[] body, string jsExpression)
    {
        using var doc = JsonDocument.Parse(body);
        var engine = new Engine(cfg => cfg.LimitRecursion(64).TimeoutInterval(TimeSpan.FromMilliseconds(50)));

        InjectJson(engine, doc.RootElement);

        var result = engine.Evaluate(jsExpression).ToObject();
        return System.Convert.ToBoolean(result);
    }

    private static void InjectJson(Engine engine, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("JSON root must be an object");

        foreach (var prop in element.EnumerateObject())
        {
            engine.SetValue(prop.Name, Convert(prop.Value));
        }
    }

    private static object? Convert(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => ToDictionary(el),
            JsonValueKind.Array => ToArray(el),
            _ => null
        };
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = Convert(prop.Value);
        return dict;
    }

    private static object?[] ToArray(JsonElement arr)
    {
        return arr.EnumerateArray().Select(Convert).ToArray();
    }

    private static FortitudeHandler ToHandler(HandlerDefinition handler)
{
    var methods = handler.Match.Methods;
    var route = handler.Match.Route;
    var headers = handler.Match.Headers;
    var queryParams = handler.Match.Query;

    // -------------------------------
    // Body predicate (composite)
    // -------------------------------
    Func<byte[]?, bool>? bodyPredicate = null;
    var bodyMatch = handler.Match.Body;
    if (bodyMatch != null)
    {
        var predicates = new List<Func<byte[]?, bool>>();

        if (!string.IsNullOrEmpty(bodyMatch.Contains))
        {
            predicates.Add(body =>
            {
                if (body == null || body.Length == 0) return false;
                var text = Encoding.UTF8.GetString(body);
                return text.Contains(bodyMatch.Contains, StringComparison.Ordinal);
            });
        }

        if (!string.IsNullOrEmpty(bodyMatch.Json))
        {
            predicates.Add(body => EvaluateJsonPredicate(body, bodyMatch.Json));
        }

        if (predicates.Count > 0)
        {
            // combine all body predicates with AND
            bodyPredicate = body => predicates.All(p => p(body));
        }
    }

    // -------------------------------
    // Request-level predicate (headers + query)
    // -------------------------------
    Func<FortitudeRequest, bool>? requestPredicate = null;
    var headerExists = handler.Match.HeaderExists ?? [];
    var queryExists = handler.Match.QueryExists ?? [];

    if ((headers?.Count ?? 0) > 0 || headerExists.Count != 0 || (queryParams?.Count ?? 0) > 0 || queryExists.Any())
    {
        requestPredicate = req =>
        {
            // Headers with exact values
            if (headers != null)
            {
                foreach (var kvp in headers)
                    if (!req.Headers.TryGetValue(kvp.Key, out var value) || !value.Contains(kvp.Value))
                        return false;
            }

            // Headers existence only
            if (headerExists.Any(key => !req.Headers.ContainsKey(key)))
            {
                return false;
            }

            // Query params exact match
            if (queryParams != null)
            {
                foreach (var kvp in queryParams)
                    if (!req.Query.TryGetValue(kvp.Key, out var value) || !value.Contains(kvp.Value))
                        return false;
            }

            // Query params existence only
            return queryExists.All(key => req.Query.ContainsKey(key));
        };
    }

    // -------------------------------
    // Responder
    // -------------------------------
    void Responder(FortitudeRequest req, FortitudeResponse res)
    {
        res.Status = handler.Response.Status;

        // Text body
        var bodyText = handler.Response.Body?.Text;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            res.Body = Encoding.UTF8.GetBytes(bodyText);
            res.ContentType = "text/plain; charset=utf-8";
        }

        // JSON body
        var jsonBody = handler.Response.Body?.Json;
        if (jsonBody != null)
        {
            res.Body = JsonSerializer.SerializeToUtf8Bytes(CoerceNumbers(jsonBody), FortitudeResponse.DefaultJsonOptions);
            res.ContentType = "application/json";
        }

        // Response headers
        var respHeaders = handler.Response.Headers;
        if (respHeaders != null)
        {
            res.Headers = respHeaders;
        }
    }

    return new FortitudeHandler(
        methods,
        route,
        headers,
        queryParams,
        bodyPredicate,
        requestPredicate,
        (Action<FortitudeRequest, FortitudeResponse>)Responder
    );
}

    
}

public sealed record FortitudeFile
{
    public List<HandlerDefinition> Handlers { get; init; } = new();
}

public sealed record HandlerDefinition
{
    public MatchDefinition Match { get; init; } = new();
    public ResponseDefinition Response { get; init; } = new();
}

public sealed record MatchDefinition
{
    // HTTP
    public List<string>? Methods { get; init; }
    public string? Route { get; init; }

    // Headers
    public Dictionary<string, string>? Headers { get; init; }
    public List<string>? HeaderExists { get; init; }

    // Query
    public Dictionary<string, string>? Query { get; init; }
    public List<string>? QueryExists { get; init; }

    // Body
    public BodyMatchDefinition? Body { get; init; }
}

public sealed record BodyMatchDefinition
{
    public string? Contains { get; init; }
    public string? Json { get; init; }

}


public sealed record ResponseDefinition
{
    public int Status { get; init; }
    public Dictionary<string, string>? Headers { get; init; }

    public ResponseBodyDefinition? Body { get; init; }
}

public sealed record ResponseBodyDefinition
{
    public Dictionary<string, object>? Json { get; init; }
    public string? Text { get; init; }
}
