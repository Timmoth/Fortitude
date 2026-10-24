using System.Net;
using System.Text.Json;
using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

/// <summary>
///     Generating handlers from an OpenAPI description.
/// </summary>
public sealed class OpenApiImportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = CreateTempDirectory();

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fortitude-openapi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private const string Petstore = """
                                    {
                                      "openapi": "3.0.0",
                                      "info": { "title": "Petstore", "version": "1.0.0" },
                                      "paths": {
                                        "/pets": {
                                          "get": {
                                            "responses": {
                                              "200": {
                                                "description": "A list of pets",
                                                "content": {
                                                  "application/json": {
                                                    "schema": {
                                                      "type": "array",
                                                      "items": { "$ref": "#/components/schemas/Pet" }
                                                    }
                                                  }
                                                }
                                              }
                                            }
                                          },
                                          "post": {
                                            "responses": {
                                              "201": {
                                                "description": "Created",
                                                "content": {
                                                  "application/json": {
                                                    "schema": { "$ref": "#/components/schemas/Pet" }
                                                  }
                                                }
                                              }
                                            }
                                          }
                                        },
                                        "/pets/{petId}": {
                                          "get": {
                                            "responses": {
                                              "200": {
                                                "description": "One pet",
                                                "content": {
                                                  "application/json": {
                                                    "example": { "id": 7, "name": "Rex" }
                                                  }
                                                }
                                              }
                                            }
                                          },
                                          "delete": {
                                            "responses": { "204": { "description": "Gone" } }
                                          }
                                        }
                                      },
                                      "components": {
                                        "schemas": {
                                          "Pet": {
                                            "type": "object",
                                            "properties": {
                                              "id": { "type": "integer" },
                                              "name": { "type": "string" },
                                              "tagged": { "type": "boolean" },
                                              "createdAt": { "type": "string", "format": "date-time" }
                                            }
                                          }
                                        }
                                      }
                                    }
                                    """;

    private static FortitudeRequest Request(string method, string route) => new()
    {
        Method = method,
        BaseUrl = "http://localhost",
        Route = route,
        Url = "http://localhost" + route,
        Protocol = "HTTP/1.1"
    };

    /// <summary>Finds the generated handler that answers a given call.</summary>
    private static FortitudeHandler Find(IReadOnlyList<FortitudeHandler> handlers, string method, string route)
    {
        var handler = FortitudeHandler.SelectFor(handlers, Request(method, route));

        Assert.True(handler != null, $"No generated handler matched {method} {route}.");
        return handler!;
    }

    private static async Task<(int Status, string Body, string ContentType)> Invoke(
        FortitudeHandler handler, string method, string route)
    {
        var response = await handler.HandleRequestAsync(Request(method, route));

        return (response.Status, System.Text.Encoding.UTF8.GetString(response.Body ?? []), response.ContentType);
    }

    // ---------------------------------------------------------------- discovery

    [Fact]
    public void EveryOperationBecomesAHandler()
    {
        var handlers = FortitudeOpenApi.FromDocument(Petstore);

        Assert.Equal(4, handlers.Count);
    }

    [Fact]
    public void RouteTemplatesSurviveIntact()
    {
        // OpenAPI's {petId} is already the syntax Fortitude's matcher uses.
        var handlers = FortitudeOpenApi.FromDocument(Petstore);

        Assert.NotNull(FortitudeHandler.SelectFor(handlers, Request("GET", "/pets/99")));
    }

    [Fact]
    public void MethodsAreDistinguished()
    {
        var handlers = FortitudeOpenApi.FromDocument(Petstore);

        // The spec documents GET and POST on /pets but not DELETE.
        Assert.NotNull(FortitudeHandler.SelectFor(handlers, Request("GET", "/pets")));
        Assert.NotNull(FortitudeHandler.SelectFor(handlers, Request("POST", "/pets")));
        Assert.Null(FortitudeHandler.SelectFor(handlers, Request("DELETE", "/pets")));
    }

    // ---------------------------------------------------------------- bodies

    [Fact]
    public async Task AnExampleInTheSpecIsUsedVerbatim()
    {
        var handler = Find(FortitudeOpenApi.FromDocument(Petstore), "GET", "/pets/7");

        var (status, body, contentType) = await Invoke(handler, "GET", "/pets/7");

        Assert.Equal(200, status);
        Assert.Equal("application/json", contentType);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Rex", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ASchemaIsSynthesisedWhenThereIsNoExample()
    {
        var handler = Find(FortitudeOpenApi.FromDocument(Petstore), "POST", "/pets");

        var (status, body, _) = await Invoke(handler, "POST", "/pets");

        Assert.Equal(201, status);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(0, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("string", json.RootElement.GetProperty("name").GetString());
        Assert.False(json.RootElement.GetProperty("tagged").GetBoolean());

        // A declared format produces something of the right shape, not just "string".
        Assert.True(DateTimeOffset.TryParse(json.RootElement.GetProperty("createdAt").GetString(), out _));
    }

    [Fact]
    public async Task AnArraySchemaYieldsOneElement()
    {
        var handler = Find(FortitudeOpenApi.FromDocument(Petstore), "GET", "/pets");

        var (_, body, _) = await Invoke(handler, "GET", "/pets");

        using var json = JsonDocument.Parse(body);

        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.Equal("string", json.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AResponseWithNoContentHasNoBody()
    {
        var handler = Find(FortitudeOpenApi.FromDocument(Petstore), "DELETE", "/pets/1");

        var (status, body, _) = await Invoke(handler, "DELETE", "/pets/1");

        Assert.Equal(204, status);
        Assert.Empty(body);
    }

    [Fact]
    public async Task TheLowestSuccessStatusIsPreferred()
    {
        var spec = """
                   {
                     "openapi": "3.0.0",
                     "paths": {
                       "/x": {
                         "get": {
                           "responses": {
                             "500": { "description": "oops" },
                             "202": { "description": "accepted" },
                             "200": { "description": "ok" }
                           }
                         }
                       }
                     }
                   }
                   """;

        var (status, _, _) = await Invoke(FortitudeOpenApi.FromDocument(spec).Single(), "GET", "/x");

        Assert.Equal(200, status);
    }

    [Fact]
    public async Task AnErrorOnlyOperationStillGetsAHandler()
    {
        var spec = """
                   {
                     "openapi": "3.0.0",
                     "paths": { "/x": { "get": { "responses": { "404": { "description": "gone" } } } } }
                   }
                   """;

        var (status, _, _) = await Invoke(FortitudeOpenApi.FromDocument(spec).Single(), "GET", "/x");

        Assert.Equal(404, status);
    }

    // ---------------------------------------------------------------- schema features

    [Fact]
    public async Task ASelfReferencingSchemaTerminates()
    {
        // A tree node pointing at itself must not recurse forever.
        var spec = """
                   {
                     "openapi": "3.0.0",
                     "paths": {
                       "/tree": {
                         "get": {
                           "responses": {
                             "200": {
                               "content": {
                                 "application/json": { "schema": { "$ref": "#/components/schemas/Node" } }
                               }
                             }
                           }
                         }
                       }
                     },
                     "components": {
                       "schemas": {
                         "Node": {
                           "type": "object",
                           "properties": {
                             "name": { "type": "string" },
                             "child": { "$ref": "#/components/schemas/Node" }
                           }
                         }
                       }
                     }
                   }
                   """;

        var (_, body, _) = await Invoke(FortitudeOpenApi.FromDocument(spec).Single(), "GET", "/tree");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("string", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AllOfBranchesAreMerged()
    {
        var spec = """
                   {
                     "openapi": "3.0.0",
                     "paths": {
                       "/x": {
                         "get": {
                           "responses": {
                             "200": {
                               "content": {
                                 "application/json": {
                                   "schema": {
                                     "allOf": [
                                       { "type": "object", "properties": { "a": { "type": "integer" } } },
                                       { "type": "object", "properties": { "b": { "type": "string" } } }
                                     ]
                                   }
                                 }
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                   """;

        var (_, body, _) = await Invoke(FortitudeOpenApi.FromDocument(spec).Single(), "GET", "/x");

        using var json = JsonDocument.Parse(body);
        Assert.Equal(0, json.RootElement.GetProperty("a").GetInt32());
        Assert.Equal("string", json.RootElement.GetProperty("b").GetString());
    }

    [Fact]
    public async Task AnEnumUsesItsFirstValue()
    {
        var spec = """
                   {
                     "openapi": "3.0.0",
                     "paths": {
                       "/x": {
                         "get": {
                           "responses": {
                             "200": {
                               "content": {
                                 "application/json": {
                                   "schema": {
                                     "type": "object",
                                     "properties": {
                                       "status": { "type": "string", "enum": ["active", "archived"] }
                                     }
                                   }
                                 }
                               }
                             }
                           }
                         }
                       }
                     }
                   }
                   """;

        var (_, body, _) = await Invoke(FortitudeOpenApi.FromDocument(spec).Single(), "GET", "/x");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------- formats and errors

    [Fact]
    public void AYamlSpecIsAccepted()
    {
        var spec = """
                   openapi: 3.0.0
                   paths:
                     /ping:
                       get:
                         responses:
                           '200':
                             content:
                               application/json:
                                 example:
                                   message: pong
                   """;

        Assert.Single(FortitudeOpenApi.FromDocument(spec));
    }

    [Fact]
    public void AnEmptyDocumentIsRejectedClearly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FortitudeOpenApi.FromDocument("   "));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void ADocumentWithNoPathsIsRejectedClearly()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FortitudeOpenApi.FromDocument("""{"openapi":"3.0.0"}"""));

        Assert.Contains("no 'paths' section", ex.Message);
    }

    [Fact]
    public void AMissingFileIsReportedByPath()
    {
        var path = Path.Combine(_dir, "nope.json");

        var ex = Assert.Throws<FileNotFoundException>(() => FortitudeOpenApi.LoadHandlers(path));

        Assert.Contains("nope.json", ex.Message);
    }

    // ---------------------------------------------------------------- integration

    [Fact]
    public async Task LoadingASpecMocksTheWholeApiOverRealHttp()
    {
        var path = Path.Combine(_dir, "petstore.json");
        File.WriteAllText(path, Petstore);

        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        var handlers = client.LoadOpenApi(path);
        Assert.Equal(4, handlers.Count);

        using var http = LiveServer.HttpClientFor(port);

        var pet = await http.GetStringAsync("/pets/7");
        Assert.Contains("Rex", pet);

        var list = await http.GetAsync("/pets");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var deleted = await http.DeleteAsync("/pets/7");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task AHandWrittenHandlerOverridesTheGeneratedOne()
    {
        // The documented workflow: load the spec for breadth, then override what the test
        // actually cares about.
        var path = Path.Combine(_dir, "petstore.json");
        File.WriteAllText(path, Petstore);

        await using var server = await LiveServer.StartAsync(portCount: 1);
        var (client, port) = await server.ConnectClientAsync(output);

        client.LoadOpenApi(path);

        client.Accepts().Get().HttpRoute("/pets/{petId}")
            .Returns((_, res) => res.Ok(new { id = 1, name = "Specific" }));

        using var http = LiveServer.HttpClientFor(port);

        Assert.Contains("Specific", await http.GetStringAsync("/pets/7"));
    }
}
