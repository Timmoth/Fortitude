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
        string? body = null) => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        RawQuery = query,
        Url = "http://localhost" + route + query,
        Protocol = "HTTP/1.1",
        Headers = headers ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
        Cookies = cookies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Body = body == null ? null : System.Text.Encoding.UTF8.GetBytes(body)
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
}
