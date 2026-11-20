using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     <see cref="FortitudeRequest.ToCurl" />, which the live UI offers as a way to replay a
///     captured request and to seed the handler generator.
/// </summary>
public class RequestToCurlTests
{
    private static FortitudeRequest Request(
        string method = "GET",
        string route = "/users",
        string query = "",
        Dictionary<string, string[]>? headers = null,
        Dictionary<string, string>? cookies = null,
        string? body = null,
        byte[]? rawBody = null) => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        RawQuery = query,
        Url = "http://localhost" + route + query,
        Protocol = "HTTP/1.1",
        Headers = headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
        Cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Body = rawBody ?? (body == null ? null : System.Text.Encoding.UTF8.GetBytes(body))
    };

    [Fact]
    public void TheMethodAndUrlAreIncluded()
    {
        var curl = Request("POST", "/users").ToCurl();

        Assert.Contains("-X POST", curl);
        Assert.Contains("http://localhost/users", curl);
    }

    [Fact]
    public void TheQueryStringSurvives()
    {
        var curl = Request(query: "?page=2&size=10").ToCurl();

        Assert.Contains("?page=2&size=10", curl);
    }

    [Fact]
    public void HeadersAreIncluded()
    {
        var curl = Request(headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = ["application/json"],
            ["X-Tenant"] = ["acme"]
        }).ToCurl();

        Assert.Contains("Accept: application/json", curl);
        Assert.Contains("X-Tenant: acme", curl);
    }

    [Fact]
    public void AMultiValuedHeaderBecomesSeveralFlags()
    {
        var curl = Request(headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = ["application/json", "text/plain"]
        }).ToCurl();

        Assert.Contains("Accept: application/json", curl);
        Assert.Contains("Accept: text/plain", curl);
    }

    [Fact]
    public void CookiesBecomeACookieHeader()
    {
        var curl = Request(cookies: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["session"] = "abc",
            ["theme"] = "dark"
        }).ToCurl();

        Assert.Contains("Cookie: session=abc; theme=dark", curl);
    }

    [Fact]
    public void ABodyIsIncluded()
    {
        var curl = Request("POST", body: """{"name":"Alice"}""").ToCurl();

        Assert.Contains("--data", curl);
        Assert.Contains("Alice", curl);
    }

    [Fact]
    public void NoBodyMeansNoDataFlag()
    {
        Assert.DoesNotContain("--data", Request().ToCurl());
    }

    [Fact]
    public void TheResultIsAccceptedByTheHandlerGenerator()
    {
        // The round trip the live UI relies on: capture a request, render it as cURL, and
        // feed that straight back in to build a handler.
        var request = Request("POST", "/orders", "?draft=true",
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Accept"] = ["application/json"]
            },
            body: """{"item":"widget"}""");

        var yaml = FortitudeYamlGenerator.CurlToMatchYaml(request.ToCurl());
        var handler = FortitudeYamlLoader.FromYamlSingle(
            yaml + "response:\n  status: 200\n");

        Assert.True(handler.Matches(new FortitudeRequest
        {
            Method = "POST",
            BaseUrl = "http://localhost",
            Route = "/orders",
            RawQuery = "?draft=true",
            Url = "http://localhost/orders?draft=true",
            Protocol = "HTTP/1.1",
            Query = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["draft"] = ["true"] },
            Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["accept"] = ["application/json"]
            }
        }));
    }

    [Fact]
    public void ABodyContainingQuotesDoesNotBreakTheCommand()
    {
        var curl = Request("POST", body: """{"quote":"he said \"hi\""}""").ToCurl();

        // The round trip through the parser is the real test: it must still find the URL
        // and method rather than choking on the quoting.
        var yaml = FortitudeYamlGenerator.CurlToMatchYaml(curl);

        Assert.Contains("/users", yaml);
        Assert.Contains("POST", yaml);
    }

    [Fact]
    public void ABodyContainingAnApostropheDoesNotBreakTheCommand()
    {
        // The body is emitted inside single quotes, where a backslash escapes nothing. The
        // apostrophe has to close the quoted run and reopen it, or everything after it is
        // parsed as separate shell words and the command silently does something else.
        var curl = Request("POST", body: """{"name":"O'Brien"}""").ToCurl();

        Assert.Contains("""O'\''Brien""", curl);
        Assert.DoesNotContain("""O\'Brien""", curl);

        var yaml = FortitudeYamlGenerator.CurlToMatchYaml(curl);

        Assert.Contains("/users", yaml);
        Assert.Contains("POST", yaml);
    }

    [Fact]
    public void AHeaderContainingAnApostropheIsEscapedTheSameWay()
    {
        var curl = Request(headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Note"] = ["it's fine"]
        }).ToCurl();

        Assert.Contains("""'X-Note: it'\''s fine'""", curl);
        Assert.Contains("/users", FortitudeYamlGenerator.CurlToMatchYaml(curl));
    }

    [Fact]
    public void ABinaryBodyIsBase64EncodedAndLabelled()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var curl = Request("POST", rawBody: png).ToCurl();

        Assert.Contains("--data-binary", curl);
        Assert.Contains(Convert.ToBase64String(png), curl);

        // A trailing '#' comment is valid shell, so the command still runs, and the reader
        // is told the bytes are encoded rather than being handed silent mojibake.
        Assert.Contains("# body was not valid UTF-8", curl);
    }

    [Fact]
    public void ABinaryBodyStillRoundTripsThroughTheGenerator()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var yaml = FortitudeYamlGenerator.CurlToMatchYaml(Request("POST", rawBody: png).ToCurl());

        Assert.Contains("/users", yaml);
        Assert.Contains("POST", yaml);
    }

    [Fact]
    public void CookiesAreNotSentTwiceWhenTheHeaderIsAlsoPresent()
    {
        // A request captured from HttpContext carries the cookies both as a raw header and
        // as the parsed dictionary. Emitting both sends every cookie twice.
        var curl = Request(
            headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cookie"] = ["session=abc"]
            },
            cookies: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["session"] = "abc"
            }).ToCurl();

        var occurrences = curl.Split("Cookie:").Length - 1;

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void AUrlContainingShellMetacharactersIsQuoted()
    {
        var curl = Request(query: "?q=a&b=$(whoami)").ToCurl();

        Assert.Contains("'http://localhost/users?q=a&b=$(whoami)'", curl);
    }
}
