using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fortitude.Client;

/// <summary>
///     Loads Fortitude handler definitions from YAML, either from files on disk or from text.
/// </summary>
/// <remarks>
///     The YAML schema is described by <see cref="FortitudeFile" /> and the definition records
///     below it. See <c>docs/yaml-handlers.md</c> for the authoring guide.
/// </remarks>
public static class FortitudeYamlLoader
{
    private sealed class TemplateContext
    {
        public Dictionary<string, string> RouteValues { get; init; } = new();
        public Dictionary<string, object?> Body { get; init; } = new();
        public Dictionary<string, string> Headers { get; init; } = new();
        public Dictionary<string, string> Query { get; init; } = new();
    }

    private static Dictionary<string, string> ExtractRouteValues(
        string template,
        string actualPath)
    {
        var templateParts = template.Trim('/').Split('/');
        var pathParts = actualPath.Trim('/').Split('/');

        if (templateParts.Length != pathParts.Length)
            return new Dictionary<string, string>();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < templateParts.Length; i++)
        {
            var part = templateParts[i];

            if (!part.StartsWith("{") || !part.EndsWith("}")) continue;
            var key = part[1..^1];
            values[key] = pathParts[i];
        }

        return values;
    }

    
    private static Dictionary<string, object?> ParseJsonBody(byte[]? body)
    {
        if (body == null || body.Length == 0)
            return new();

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ToDictionary(doc.RootElement);
        }
        catch
        {
            return new();
        }
    }
    
    private static string RenderTemplate(string template, TemplateContext ctx)
    {
        return Regex.Replace(template, @"\{\{\s*(.*?)\s*\}\}", match =>
        {
            var expression = match.Groups[1].Value.Trim();
            if (expression.Length == 0) return string.Empty;

            // An expression is either a dotted path into the request, or a built-in function
            // with optional space-separated arguments.
            var parts = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = parts[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (path.Length == 0) return string.Empty;

            object? current = path[0] switch
            {
                "body" => ctx.Body,
                "headers" => ctx.Headers,
                "query" => ctx.Query,
                "route" => ctx.RouteValues,
                _ => null
            };

            if (current == null)
                return RenderFunction(path, parts.Skip(1).ToArray());

            foreach (var segment in path.Skip(1))
            {
                current = current switch
                {
                    Dictionary<string, object?> d when d.TryGetValue(segment, out var v) => v,
                    Dictionary<string, string> d when d.TryGetValue(segment, out var v) => v,
                    _ => null
                };

                if (current == null)
                    return string.Empty;
            }

            return current?.ToString() ?? string.Empty;
        });
    }

    /// <summary>
    ///     Evaluates a built-in template function, for values that must vary per response.
    /// </summary>
    /// <param name="path">The function name, split on dots, e.g. <c>now.unix</c>.</param>
    /// <param name="args">Space-separated arguments, e.g. the bounds of <c>randomInt</c>.</param>
    /// <returns>The rendered value, or an empty string if the function is unknown.</returns>
    private static string RenderFunction(string[] path, string[] args)
    {
        var name = path[0].ToLowerInvariant();
        var qualifier = path.Length > 1 ? path[1].ToLowerInvariant() : null;

        switch (name)
        {
            case "uuid":
            case "guid":
                return Guid.NewGuid().ToString();

            case "now":
                var now = DateTimeOffset.UtcNow;
                return qualifier switch
                {
                    "unix" => now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    "unixms" => now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    "date" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "time" => now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    null or "iso8601" => now.ToString("O", CultureInfo.InvariantCulture),
                    _ => now.ToString(path[1], CultureInfo.InvariantCulture)
                };

            case "randomint":
                var min = args.Length > 0 && int.TryParse(args[0], out var parsedMin) ? parsedMin : 0;
                var max = args.Length > 1 && int.TryParse(args[1], out var parsedMax) ? parsedMax : int.MaxValue;
                if (max < min) (min, max) = (max, min);
                return Random.Shared.Next(min, max == int.MaxValue ? max : max + 1)
                    .ToString(CultureInfo.InvariantCulture);

            default:
                // Unknown names render empty, matching how an unresolved path behaves.
                return string.Empty;
        }
    }
    
    /// <summary>
    ///     Walks a deserialized response body, rendering templates and normalising the result
    ///     into shapes <see cref="JsonSerializer" /> can emit.
    /// </summary>
    /// <remarks>
    ///     YamlDotNet produces <c>Dictionary&lt;object, object&gt;</c> for nested mappings while
    ///     the top level is typed as <c>Dictionary&lt;string, object&gt;</c>, so both have to be
    ///     handled or templates below the first level silently fail to render.
    /// </remarks>
    private static object? RenderJsonTemplates(object? value, TemplateContext ctx)
    {
        switch (value)
        {
            case null:
                return null;

            case string s:
                return CoerceScalar(RenderTemplate(s, ctx));

            case IDictionary<object, object?> objectKeyed:
                return TryReadBareTemplate(objectKeyed, out var bare)
                    ? CoerceScalar(RenderTemplate(bare, ctx))
                    : objectKeyed.ToDictionary(
                        kvp => kvp.Key?.ToString() ?? string.Empty,
                        kvp => RenderJsonTemplates(kvp.Value, ctx));

            case IDictionary<string, object?> stringKeyed:
                return stringKeyed.ToDictionary(
                    kvp => kvp.Key,
                    kvp => RenderJsonTemplates(kvp.Value, ctx));

            case IEnumerable<object?> sequence:
                return sequence.Select(v => RenderJsonTemplates(v, ctx)).ToList();

            default:
                return value;
        }
    }

    /// <summary>
    ///     Recognises a template written without quotes, e.g. <c>id: {{route.id}}</c>.
    /// </summary>
    /// <remarks>
    ///     YAML reads <c>{{route.id}}</c> as a flow mapping containing a flow mapping, not as
    ///     text: a single-entry map whose value is null and whose key is itself a single-entry
    ///     map with a null value. Nothing a real response body would contain looks like that,
    ///     so it can be safely reconstructed back into the template the author intended.
    /// </remarks>
    private static bool TryReadBareTemplate(IDictionary<object, object?> map, out string template)
    {
        template = string.Empty;

        if (map.Count != 1) return false;

        var outer = map.First();
        if (outer.Value != null) return false;
        if (outer.Key is not IDictionary<object, object?> innerMap || innerMap.Count != 1) return false;

        var inner = innerMap.First();
        if (inner.Value != null) return false;
        if (inner.Key is not string expression) return false;

        template = "{{" + expression + "}}";
        return true;
    }

    /// <summary>
    ///     Converts a scalar to the JSON type it reads as, so that <c>"{{route.id}}"</c>
    ///     against <c>/users/42</c> emits <c>42</c> rather than <c>"42"</c>, and a literal
    ///     <c>flag: true</c> emits a boolean rather than the string <c>"true"</c>.
    /// </summary>
    /// <remarks>
    ///     YamlDotNet hands back every untyped scalar as a string, so the author's intent has
    ///     to be inferred here. Values with a redundant leading zero (<c>01234</c>) are left as
    ///     strings: those are nearly always identifiers, postcodes or phone numbers that would
    ///     be corrupted by being read as a number.
    /// </remarks>
    private static object CoerceScalar(string value)
    {
        if (value.Length == 0)
            return value;

        if (bool.TryParse(value, out var b))
            return b;

        if (HasRedundantLeadingZero(value))
            return value;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;

        return value;
    }

    /// <summary>"01234" yes, "0" no, "0.5" no.</summary>
    private static bool HasRedundantLeadingZero(string value)
    {
        var digits = value.StartsWith('-') || value.StartsWith('+') ? value[1..] : value;

        return digits.Length > 1 && digits[0] == '0' && digits[1] != '.';
    }
    
    /// <summary>File extensions recognised as handler definitions.</summary>
    public static readonly string[] YamlExtensions = [".yaml", ".yml"];

    /// <summary>
    ///     Enumerates handler definition files under <paramref name="dir" />, recursively.
    /// </summary>
    /// <remarks>
    ///     Both <c>.yaml</c> and <c>.yml</c> are recognised. Results are ordered by path so the
    ///     last-match-wins handler precedence is deterministic across runs and platforms.
    /// </remarks>
    public static IEnumerable<string> EnumerateYamlFiles(string dir)
    {
        if (!Directory.Exists(dir)) return [];

        return Directory
            .EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => YamlExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Loads every handler file under <paramref name="dir" />, reporting per-file outcomes.
    /// </summary>
    /// <remarks>
    ///     A file that fails to parse yields a result carrying its <see cref="YamlHandlerFile.Error" />
    ///     rather than throwing, so one malformed file cannot stop the server from starting or
    ///     hide the handlers defined in its siblings.
    /// </remarks>
    public static IReadOnlyList<YamlHandlerFile> LoadHandlerFiles(string dir)
    {
        var results = new List<YamlHandlerFile>();

        foreach (var file in EnumerateYamlFiles(dir))
        {
            try
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var handlers = FromYamlMulti(text, Path.GetDirectoryName(file)).ToList();
                results.Add(new YamlHandlerFile(file, handlers, null));
            }
            catch (Exception ex)
            {
                results.Add(new YamlHandlerFile(file, [], Describe(ex)));
            }
        }

        return results;
    }

    /// <summary>
    ///     Loads every handler defined under <paramref name="dir" />, skipping files that fail
    ///     to parse. Use <see cref="LoadHandlerFiles" /> when you need to surface the failures.
    /// </summary>
    public static IEnumerable<FortitudeHandler> LoadHandlers(string dir)
    {
        return LoadHandlerFiles(dir).SelectMany(f => f.Handlers);
    }

    /// <summary>Content types inferred from a fixture file's extension.</summary>
    private static readonly Dictionary<string, string> ContentTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".csv"] = "text/csv",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif"
    };

    /// <summary>Extensions treated as text, and therefore templated.</summary>
    private static readonly string[] TextExtensions =
        [".json", ".xml", ".html", ".htm", ".txt", ".csv", ".yaml", ".yml"];

    /// <summary>
    ///     Loads a fixture file into the response body.
    /// </summary>
    /// <remarks>
    ///     A missing or unreadable file produces a 500 describing the problem rather than
    ///     throwing, so one broken fixture does not take down every request the server handles.
    /// </remarks>
    private static void WriteFileBody(
        FortitudeResponse res,
        string path,
        string? contentType,
        string? baseDirectory,
        TemplateContext ctx)
    {
        var resolved = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), path));

        if (!File.Exists(resolved))
        {
            res.InternalServerError($"Fortitude: response body file not found: {resolved}");
            return;
        }

        var extension = Path.GetExtension(resolved);

        try
        {
            if (TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                var text = File.ReadAllText(resolved, Encoding.UTF8);
                res.Body = Encoding.UTF8.GetBytes(RenderTemplate(text, ctx));
            }
            else
            {
                res.Body = File.ReadAllBytes(resolved);
            }
        }
        catch (Exception ex)
        {
            res.InternalServerError($"Fortitude: could not read response body file {resolved}: {ex.Message}");
            return;
        }

        res.ContentType = contentType
                          ?? (ContentTypesByExtension.TryGetValue(extension, out var inferred)
                              ? inferred
                              : "application/octet-stream");
    }

    /// <summary>Flattens a YAML parse failure into a single readable line.</summary>
    private static string Describe(Exception ex)
    {
        var message = ex.Message;

        // YamlDotNet nests the useful detail (line/column, the offending node) one level down.
        if (ex.InnerException != null)
            message += " -> " + ex.InnerException.Message;

        return message.ReplaceLineEndings(" ");
    }
    
    /// <summary>
    ///     Parses a single handler definition — a document with top level <c>match</c> and
    ///     <c>response</c> keys, without the enclosing <c>handlers:</c> sequence.
    /// </summary>
    public static FortitudeHandler FromYamlSingle(string yaml, string? baseDirectory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var handler = deserializer.Deserialize<HandlerDefinition>(yaml);
        return ToHandler(handler, baseDirectory);
    }
    
    /// <summary>
    ///     Parses a handler definition document: a <c>handlers:</c> sequence of
    ///     <c>match</c>/<c>response</c> pairs.
    /// </summary>
    public static IEnumerable<FortitudeHandler> FromYamlMulti(string yaml, string? baseDirectory = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // An empty or comment-only document deserializes to null.
        var handlerDefs = deserializer.Deserialize<FortitudeFile>(yaml)?.Handlers;
        if (handlerDefs == null)
            yield break;

        foreach (var handlerDef in handlerDefs)
        {
            yield return ToHandler(handlerDef, baseDirectory);
        }
    }

    private static bool EvaluateJsonPredicate(byte[]? body, string jsExpression)
    {
        // Matching must never throw: a body that is not JSON, or not a JSON object, simply
        // does not match. Letting an exception escape turns a non-match into a 500.
        if (body == null || body.Length == 0)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var engine = new Engine(cfg => cfg.LimitRecursion(64).TimeoutInterval(TimeSpan.FromMilliseconds(50)));

            InjectJson(engine, doc.RootElement);

            var result = engine.Evaluate(jsExpression).ToObject();
            return System.Convert.ToBoolean(result);
        }
        catch
        {
            return false;
        }
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

    private static FortitudeHandler ToHandler(HandlerDefinition handler, string? baseDirectory = null)
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
    // Request-level predicate (presence-only header and query checks)
    // -------------------------------
    // Exact header and query values are enforced by the handler itself from the `headers`
    // and `queryParams` arguments below, so only the existence checks need a predicate.
    Func<FortitudeRequest, bool>? requestPredicate = null;
    var headerExists = handler.Match.HeaderExists ?? [];
    var queryExists = handler.Match.QueryExists ?? [];

    if (headerExists.Count > 0 || queryExists.Count > 0)
    {
        requestPredicate = req =>
            headerExists.All(key => req.Headers.ContainsKey(key))
            && queryExists.All(key => req.Query.ContainsKey(key));
    }

    // -------------------------------
    // Responder
    // -------------------------------
    void Responder(FortitudeRequest req, FortitudeResponse res)
    {
        res.Status = handler.Response.Status;

        var templateContext = new TemplateContext
        {
            Body = ParseJsonBody(req.Body),
            Headers = req.Headers.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FirstOrDefault() ?? string.Empty
            ),
            Query = req.Query.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FirstOrDefault() ?? string.Empty
            ),
            RouteValues = string.IsNullOrEmpty(handler.Match.Route) ? new Dictionary<string, string>() : ExtractRouteValues(handler.Match.Route, req.Route)
        };

        // File-backed body: takes precedence over inline text and JSON.
        var bodyFile = handler.Response.Body?.File;
        if (!string.IsNullOrWhiteSpace(bodyFile))
        {
            WriteFileBody(res, bodyFile, handler.Response.Body?.ContentType, baseDirectory, templateContext);
            ApplyResponseHeaders(res, handler, templateContext);
            return;
        }

        // Text body
        var bodyText = handler.Response.Body?.Text;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            var rendered = RenderTemplate(bodyText, templateContext);
            res.Body = Encoding.UTF8.GetBytes(rendered);
            res.ContentType = "text/plain; charset=utf-8";
        }

        // JSON body
        var jsonBody = handler.Response.Body?.Json;
        if (jsonBody != null)
        {
            var renderedJson = RenderJsonTemplates(jsonBody, templateContext);

            res.Body = JsonSerializer.SerializeToUtf8Bytes(renderedJson, JsonSerializerOptions.Web);
            res.ContentType = "application/json";
        }

        ApplyResponseHeaders(res, handler, templateContext);
    }

    static void ApplyResponseHeaders(FortitudeResponse res, HandlerDefinition definition, TemplateContext ctx)
    {
        if (definition.Response.Headers == null) return;

        res.Headers = definition.Response.Headers.ToDictionary(
            kvp => kvp.Key,
            kvp => RenderTemplate(kvp.Value, ctx)
        );
    }
    


    var built = new FortitudeHandler(
        methods,
        route,
        headers,
        queryParams,
        bodyPredicate,
        requestPredicate,
        (Action<FortitudeRequest, FortitudeResponse>)Responder
    );

    if (handler.Response.Delay is { } range)
    {
        built.Delay = TimeSpan.FromMilliseconds(Math.Max(0, range.MinMs));
        built.DelayMax = TimeSpan.FromMilliseconds(Math.Max(range.MinMs, range.MaxMs));
    }
    else if (handler.Response.DelayMs is { } fixedDelay)
    {
        built.Delay = TimeSpan.FromMilliseconds(Math.Max(0, fixedDelay));
    }

    return built;
}

    
}

/// <summary>
///     A handler definition document — the top level shape of a <c>.yaml</c> handler file.
/// </summary>
public sealed record FortitudeFile
{
    /// <summary>The handlers defined in this file, in document order.</summary>
    public List<HandlerDefinition> Handlers { get; init; } = new();
}

/// <summary>
///     One handler: the requests it matches, and the response it returns for them.
/// </summary>
public sealed record HandlerDefinition
{
    /// <summary>Criteria an incoming request must satisfy. All criteria must match.</summary>
    public MatchDefinition Match { get; init; } = new();

    /// <summary>The response returned when <see cref="Match" /> is satisfied.</summary>
    public ResponseDefinition Response { get; init; } = new();
}

/// <summary>
///     Criteria an incoming request must satisfy for a handler to answer it. Every criterion
///     present must match; omitted criteria are not checked.
/// </summary>
public sealed record MatchDefinition
{
    /// <summary>HTTP methods to match, e.g. <c>[GET, HEAD]</c>. Omit to match any method.</summary>
    public List<string>? Methods { get; init; }

    /// <summary>
    ///     Route to match, supporting ASP.NET route templates such as <c>/users/{id}</c>.
    ///     Captured segments are available to response templates as <c>{{route.id}}</c>.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>Headers that must be present with these exact values. Names are case insensitive.</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Headers that must be present, whatever their value.</summary>
    public List<string>? HeaderExists { get; init; }

    /// <summary>Query parameters that must be present with these exact values.</summary>
    public Dictionary<string, string>? Query { get; init; }

    /// <summary>Query parameters that must be present, whatever their value.</summary>
    public List<string>? QueryExists { get; init; }

    /// <summary>Conditions on the request body.</summary>
    public BodyMatchDefinition? Body { get; init; }
}

/// <summary>
///     Conditions on a request body. When both are given, both must hold.
/// </summary>
public sealed record BodyMatchDefinition
{
    /// <summary>Text the body must contain, compared ordinally.</summary>
    public string? Contains { get; init; }

    /// <summary>
    ///     A JavaScript expression evaluated against the parsed JSON body, e.g.
    ///     <c>email == "alice@example.com"</c>. Top level properties are in scope as
    ///     variables. A body that is not a JSON object simply does not match.
    /// </summary>
    public string? Json { get; init; }
}


/// <summary>
///     The response a handler returns.
/// </summary>
public sealed record ResponseDefinition
{
    /// <summary>HTTP status code to return.</summary>
    public int Status { get; init; }

    /// <summary>Response headers. Values support <c>{{ }}</c> templating.</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>The response body. Omit for an empty body.</summary>
    public ResponseBodyDefinition? Body { get; init; }

    /// <summary>
    ///     Milliseconds to wait before responding, simulating a slow dependency. Ignored when
    ///     <see cref="Delay" /> is given.
    /// </summary>
    public int? DelayMs { get; init; }

    /// <summary>A randomised delay range, for simulating jitter.</summary>
    public DelayDefinition? Delay { get; init; }
}

/// <summary>
///     A randomised response delay, drawn uniformly from the given range.
/// </summary>
public sealed record DelayDefinition
{
    /// <summary>Lower bound in milliseconds.</summary>
    public int MinMs { get; init; }

    /// <summary>Upper bound in milliseconds.</summary>
    public int MaxMs { get; init; }
}

/// <summary>
///     A response body, as either JSON or plain text. If both are given, JSON wins.
/// </summary>
public sealed record ResponseBodyDefinition
{
    /// <summary>
    ///     A JSON body, served as <c>application/json</c>. Values at any depth support
    ///     <c>{{ }}</c> templating, quoted or not.
    /// </summary>
    public Dictionary<string, object>? Json { get; init; }

    /// <summary>
    ///     A plain text body, served as <c>text/plain</c>. Supports <c>{{ }}</c> templating.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    ///     Path to a file whose contents become the body, resolved relative to the handler
    ///     definition file. Keeps large fixtures out of the YAML.
    /// </summary>
    /// <remarks>
    ///     The content type is inferred from the extension, and <c>{{ }}</c> templating is
    ///     applied to text formats. Takes precedence over <see cref="Text" /> and
    ///     <see cref="Json" />.
    /// </remarks>
    public string? File { get; init; }

    /// <summary>
    ///     Content type for <see cref="File" />, overriding what the extension implies.
    /// </summary>
    public string? ContentType { get; init; }
}

/// <summary>
///     The outcome of loading one YAML handler definition file.
/// </summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="Handlers">Handlers parsed from it; empty when <paramref name="Error" /> is set.</param>
/// <param name="Error">Parse failure description, or null when the file loaded cleanly.</param>
public sealed record YamlHandlerFile(string Path, IReadOnlyList<FortitudeHandler> Handlers, string? Error)
{
    /// <summary>File name without its directory.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Whether the file failed to parse.</summary>
    public bool HasError => Error != null;
}
