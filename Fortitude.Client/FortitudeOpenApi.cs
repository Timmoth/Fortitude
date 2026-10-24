using System.Globalization;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Fortitude.Client;

/// <summary>
///     Builds handlers from an OpenAPI description, turning "write a mock" into "point at the
///     spec".
/// </summary>
/// <remarks>
///     Supports OpenAPI 3.x in JSON or YAML. For each path and method it generates a handler
///     returning that operation's documented success response: an <c>example</c> if the spec
///     provides one, otherwise a body synthesised from the response schema.
///     <para>
///         The result is a starting point, not a faithful implementation. Generated handlers
///         are registered first, so anything you define yourself afterwards takes precedence
///         for the routes you care about.
///     </para>
/// </remarks>
public static class FortitudeOpenApi
{
    private static readonly string[] HttpMethods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>
    ///     Reads an OpenAPI document from disk and builds handlers from it.
    /// </summary>
    /// <param name="path">Path to a <c>.json</c>, <c>.yaml</c> or <c>.yml</c> description.</param>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the document cannot be understood.</exception>
    public static IReadOnlyList<FortitudeHandler> LoadHandlers(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"OpenAPI document not found: {path}", path);

        return FromDocument(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>
    ///     Builds handlers from an OpenAPI document supplied as text, in JSON or YAML.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the document cannot be understood.</exception>
    public static IReadOnlyList<FortitudeHandler> FromDocument(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("The OpenAPI document is empty.");

        using var document = Parse(content);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The OpenAPI document must be an object.");

        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "The OpenAPI document has no 'paths' section, so there is nothing to mock.");

        // Keep the whole document around: schemas are referenced by $ref into components.
        var handlers = new List<FortitudeHandler>();

        foreach (var path in paths.EnumerateObject())
        foreach (var method in HttpMethods)
        {
            if (!path.Value.TryGetProperty(method, out var operation)) continue;

            handlers.Add(BuildHandler(path.Name, method, operation, root));
        }

        return handlers;
    }

    /// <summary>Parses JSON directly, or YAML by way of JSON.</summary>
    private static JsonDocument Parse(string content)
    {
        var trimmed = content.TrimStart();

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                return JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"The OpenAPI document is not valid JSON: {ex.Message}", ex);
            }
        }

        try
        {
            var yaml = new DeserializerBuilder().Build().Deserialize<object?>(content);
            return JsonDocument.Parse(JsonSerializer.Serialize(yaml));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"The OpenAPI document is not valid YAML: {ex.Message}", ex);
        }
    }

    private static FortitudeHandler BuildHandler(
        string route,
        string method,
        JsonElement operation,
        JsonElement root)
    {
        var (status, body, contentType) = ResolveResponse(operation, root);

        return FortitudeHandler.Accepts()
            .Method(method)
            .HttpRoute(route)
            // Below the default so anything hand-written for the same route wins.
            .Priority(-1000)
            .Returns((_, res) =>
            {
                res.Status = status;

                if (body == null)
                {
                    res.ClearBody();
                    return;
                }

                res.Body = Encoding.UTF8.GetBytes(body);
                res.ContentType = contentType;
            });
    }

    /// <summary>
    ///     Picks the response to mock and produces its body.
    /// </summary>
    private static (int Status, string? Body, string ContentType) ResolveResponse(
        JsonElement operation,
        JsonElement root)
    {
        if (!operation.TryGetProperty("responses", out var responses)
            || responses.ValueKind != JsonValueKind.Object)
            return (200, null, "application/json");

        var chosen = ChooseResponse(responses);

        if (chosen is not { } response)
            return (200, null, "application/json");

        var status = response.Status;

        if (!response.Value.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object)
            return (status, null, "application/json");

        // Prefer JSON; otherwise take whatever the operation does offer.
        var media = content.EnumerateObject()
            .FirstOrDefault(m => m.Name.Contains("json", StringComparison.OrdinalIgnoreCase));

        if (media.Value.ValueKind != JsonValueKind.Object)
            media = content.EnumerateObject().FirstOrDefault();

        if (media.Value.ValueKind != JsonValueKind.Object)
            return (status, null, "application/json");

        var contentType = media.Name;
        var body = ResolveBody(media.Value, root);

        return (status, body, contentType);
    }

    /// <summary>
    ///     Chooses the most useful documented response: a 200 or 201 if present, otherwise the
    ///     lowest 2xx, otherwise the lowest status of any kind.
    /// </summary>
    private static (int Status, JsonElement Value)? ChooseResponse(JsonElement responses)
    {
        var candidates = new List<(int Status, JsonElement Value)>();

        foreach (var response in responses.EnumerateObject())
        {
            if (int.TryParse(response.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var status))
                candidates.Add((status, response.Value));
            else if (response.Name.Equals("2XX", StringComparison.OrdinalIgnoreCase))
                candidates.Add((200, response.Value));
            else if (response.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
                candidates.Add((200, response.Value));
        }

        if (candidates.Count == 0) return null;

        var success = candidates.Where(c => c.Status is >= 200 and <= 299).ToList();

        return success.Count > 0
            ? success.OrderBy(c => c.Status).First()
            : candidates.OrderBy(c => c.Status).First();
    }

    /// <summary>
    ///     Produces a body from a media type object: its example if there is one, otherwise a
    ///     value synthesised from the schema.
    /// </summary>
    private static string? ResolveBody(JsonElement media, JsonElement root)
    {
        // A spec-provided example is always better than anything we can invent.
        if (media.TryGetProperty("example", out var example))
            return example.GetRawText();

        if (media.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object)
        {
            var first = examples.EnumerateObject().FirstOrDefault();

            if (first.Value.ValueKind == JsonValueKind.Object
                && first.Value.TryGetProperty("value", out var value))
                return value.GetRawText();
        }

        if (!media.TryGetProperty("schema", out var schema))
            return null;

        var synthesised = Synthesise(schema, root, new HashSet<string>(), 0);

        return synthesised == null ? null : JsonSerializer.Serialize(synthesised);
    }

    /// <summary>
    ///     Builds a plausible value for a schema, resolving local <c>$ref</c>s.
    /// </summary>
    /// <param name="schema">The schema to produce a value for.</param>
    /// <param name="root">The whole document, for resolving references.</param>
    /// <param name="visited">
    ///     Refs already being expanded, so a self-referencing schema terminates instead of
    ///     recursing forever.
    /// </param>
    /// <param name="depth">Guards against deeply nested or mutually recursive schemas.</param>
    private static object? Synthesise(JsonElement schema, JsonElement root, HashSet<string> visited, int depth)
    {
        if (depth > 10 || schema.ValueKind != JsonValueKind.Object)
            return null;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var pointer = reference.GetString();

            if (pointer == null || !visited.Add(pointer))
                return null;

            var resolved = ResolveRef(pointer, root);
            var result = resolved is { } target ? Synthesise(target, root, visited, depth + 1) : null;

            visited.Remove(pointer);
            return result;
        }

        // An explicit example on the schema beats anything synthesised.
        if (schema.TryGetProperty("example", out var example))
            return JsonSerializer.Deserialize<object?>(example.GetRawText());

        if (schema.TryGetProperty("default", out var defaultValue))
            return JsonSerializer.Deserialize<object?>(defaultValue.GetRawText());

        if (schema.TryGetProperty("enum", out var enumValues)
            && enumValues.ValueKind == JsonValueKind.Array
            && enumValues.GetArrayLength() > 0)
            return JsonSerializer.Deserialize<object?>(enumValues[0].GetRawText());

        // allOf is a composition: merge every branch into one object.
        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            var merged = new Dictionary<string, object?>();

            foreach (var branch in allOf.EnumerateArray())
                if (Synthesise(branch, root, visited, depth + 1) is Dictionary<string, object?> part)
                    foreach (var pair in part)
                        merged[pair.Key] = pair.Value;

            return merged;
        }

        foreach (var composite in (string[]) ["oneOf", "anyOf"])
            if (schema.TryGetProperty(composite, out var branches)
                && branches.ValueKind == JsonValueKind.Array
                && branches.GetArrayLength() > 0)
                return Synthesise(branches[0], root, visited, depth + 1);

        var type = schema.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

        // A schema with properties but no declared type is an object in practice.
        if (type == null && schema.TryGetProperty("properties", out _))
            type = "object";

        switch (type)
        {
            case "object":
            {
                var result = new Dictionary<string, object?>();

                if (schema.TryGetProperty("properties", out var properties)
                    && properties.ValueKind == JsonValueKind.Object)
                    foreach (var property in properties.EnumerateObject())
                        result[property.Name] = Synthesise(property.Value, root, visited, depth + 1);

                return result;
            }

            case "array":
                // One element is enough to show the shape without inventing volume.
                return schema.TryGetProperty("items", out var items)
                    ? new List<object?> { Synthesise(items, root, visited, depth + 1) }
                    : new List<object?>();

            case "integer":
                return 0;

            case "number":
                return 0.0;

            case "boolean":
                return false;

            case "string":
                return SampleString(schema);

            case "null":
                return null;

            default:
                return null;
        }
    }

    /// <summary>Produces a string that respects the schema's declared format.</summary>
    private static string SampleString(JsonElement schema)
    {
        var format = schema.TryGetProperty("format", out var formatElement)
            ? formatElement.GetString()
            : null;

        return format switch
        {
            "date-time" => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "date" => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "uuid" => Guid.Empty.ToString(),
            "email" => "user@example.com",
            "uri" or "url" => "https://example.com",
            "hostname" => "example.com",
            "ipv4" => "127.0.0.1",
            "byte" => Convert.ToBase64String("string"u8.ToArray()),
            _ => "string"
        };
    }

    /// <summary>Resolves a local JSON pointer such as <c>#/components/schemas/User</c>.</summary>
    private static JsonElement? ResolveRef(string pointer, JsonElement root)
    {
        // External references would mean fetching another document; out of scope.
        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
            return null;

        var current = root;

        foreach (var rawSegment in pointer[2..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Replace("~1", "/").Replace("~0", "~");

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;

            current = next;
        }

        return current;
    }
}

/// <summary>
///     Registers OpenAPI-derived handlers on a client.
/// </summary>
public static class FortitudeOpenApiExtensions
{
    /// <summary>
    ///     Loads an OpenAPI description and registers a handler for every operation in it.
    /// </summary>
    /// <param name="client">The client to register against.</param>
    /// <param name="path">Path to a <c>.json</c>, <c>.yaml</c> or <c>.yml</c> description.</param>
    /// <returns>The handlers that were registered.</returns>
    /// <remarks>
    ///     Generated handlers sit below the default priority, so a handler you register
    ///     afterwards for the same route takes precedence. Load the spec first, then override
    ///     the operations the test actually cares about.
    /// </remarks>
    public static IReadOnlyList<FortitudeHandler> LoadOpenApi(this FortitudeClient client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);

        var handlers = FortitudeOpenApi.LoadHandlers(path);

        foreach (var handler in handlers)
            client.AddHandler(handler);

        return handlers;
    }
}
