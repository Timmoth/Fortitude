using Fortitude.Client;
using Xunit;

namespace Tests;

/// <summary>
///     Response-body templating: which shapes render, and which YAML spellings are accepted.
/// </summary>
public class YamlTemplatingTests
{
    private static async Task<string> Render(string yaml, string route, string? jsonBody = null)
    {
        var handler = FortitudeYamlLoader.FromYamlSingle(yaml);

        var request = new FortitudeRequest
        {
            Method = jsonBody == null ? "GET" : "POST",
            BaseUrl = "http://localhost",
            Route = route,
            Url = "http://localhost" + route,
            Protocol = "HTTP/1.1",
            Body = jsonBody == null ? null : System.Text.Encoding.UTF8.GetBytes(jsonBody)
        };

        var response = await handler.HandleRequestAsync(request);
        return System.Text.Encoding.UTF8.GetString(response.Body ?? []);
    }

    [Fact]
    public async Task AQuotedTemplate_AtTheTopLevel_Renders()
    {
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      userId: "{{route.id}}"
                                """, "/users/42");

        Assert.Equal("""{"userId":42}""", body);
    }

    [Fact]
    public async Task AnUnquotedTemplate_Renders()
    {
        // This is the spelling the README documents. YAML parses {{x}} as a nested flow
        // mapping rather than a string, so it needs explicit handling.
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      userId: {{route.id}}
                                """, "/users/42");

        Assert.Equal("""{"userId":42}""", body);
    }

    [Fact]
    public async Task ATemplateInsideANestedObject_Renders()
    {
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      user:
                                        id: "{{route.id}}"
                                        name: "{{body.name}}"
                                """, "/users/7", """{"name":"Alice"}""");

        Assert.Equal("""{"user":{"id":7,"name":"Alice"}}""", body);
    }

    [Fact]
    public async Task ATemplateInsideAnArray_Renders()
    {
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      ids:
                                        - "{{route.id}}"
                                      people:
                                        - name: "{{body.name}}"
                                """, "/users/3", """{"name":"Bob"}""");

        Assert.Equal("""{"ids":[3],"people":[{"name":"Bob"}]}""", body);
    }

    [Fact]
    public async Task ADeeplyNestedTemplate_Renders()
    {
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      a:
                                        b:
                                          c: "{{route.id}}"
                                """, "/users/9");

        Assert.Equal("""{"a":{"b":{"c":9}}}""", body);
    }

    [Fact]
    public async Task AnUnresolvedTemplate_RendersEmpty()
    {
        var body = await Render("""
                                match:
                                  route: /users/{id}
                                response:
                                  status: 200
                                  body:
                                    json:
                                      missing: "{{body.nope}}"
                                """, "/users/1");

        Assert.Equal("""{"missing":""}""", body);
    }

    [Fact]
    public async Task LiteralValues_SurviveUntouched()
    {
        var body = await Render("""
                                match:
                                  route: /thing
                                response:
                                  status: 200
                                  body:
                                    json:
                                      name: Alice
                                      count: 3
                                      nested:
                                        flag: true
                                """, "/thing");

        Assert.Contains("\"name\":\"Alice\"", body);
        Assert.Contains("\"count\":3", body);
        Assert.Contains("\"flag\":true", body);
    }

    [Fact]
    public async Task ValuesWithALeadingZero_StayStrings()
    {
        // A postcode or account number must not be mangled into a number.
        var body = await Render("""
                                match:
                                  route: /thing
                                response:
                                  status: 200
                                  body:
                                    json:
                                      postcode: "01234"
                                      zero: "0"
                                      fraction: "0.5"
                                """, "/thing");

        Assert.Contains("\"postcode\":\"01234\"", body);
        Assert.Contains("\"zero\":0", body);
        Assert.Contains("\"fraction\":0.5", body);
    }
}
