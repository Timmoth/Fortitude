# Mocking from an OpenAPI description

Writing handlers by hand is fine for the two or three endpoints a test actually cares about.
It is tedious for the other forty that just need to exist. `LoadOpenApi` reads a spec and
generates a handler for every operation in it.

```csharp
var fortitude = FortitudeClient.Create(output);

fortitude.LoadOpenApi("./petstore.json");
```

That is the whole API surface. OpenAPI 3.x is supported, in JSON or YAML.

## What it generates

One handler per path and method. OpenAPI's `{petId}` placeholders are already the syntax
Fortitude matches on, so `/pets/{petId}` works unchanged.

For each operation it picks the most useful documented response - a `200` or `201` if there is
one, otherwise the lowest `2xx`, otherwise the lowest status of any kind - and produces a body:

1. The response's `example`, if the spec provides one. A real example always beats a guess.
2. The first entry under `examples`.
3. A body synthesised from the response `schema`.

Synthesis resolves local `$ref`s into `components/schemas`, merges `allOf` branches, takes the
first branch of `oneOf`/`anyOf`, and uses the first value of an `enum`. Scalars become a
representative value for their type, and a declared `format` is respected:

| Schema | Generated |
|---|---|
| `integer` | `0` |
| `number` | `0.0` |
| `boolean` | `false` |
| `string` | `"string"` |
| `string`, `format: date-time` | the current timestamp |
| `string`, `format: uuid` | an empty GUID |
| `string`, `format: email` | `user@example.com` |
| `array` | a one-element array |

A schema that refers to itself terminates rather than recursing, so a tree or linked-list model
will not hang the loader.

## Overriding what matters

Generated handlers are registered below the default priority. Anything you register afterwards
for the same route wins, so the intended workflow is: load the spec for breadth, then write
real handlers for the endpoints under test.

```csharp
fortitude.LoadOpenApi("./petstore.json");

// Everything else still answers from the spec; this one does what the test needs.
fortitude.Accepts().Get().HttpRoute("/pets/{petId}")
    .Returns((req, res) => res.Ok(new Pet(7, "Rex")));
```

## Limitations

This is a starting point, not a faithful implementation of your API.

- **OpenAPI 3.x only.** Swagger 2.0 documents are not recognised.
- **Local references only.** A `$ref` to another file or a URL is not followed; the property
  it describes comes out as `null`.
- **No request validation.** Generated handlers match on method and path, not on whether the
  request body conforms to the spec.
- **One response per operation.** Status-code-specific behaviour needs a handler of your own.
- **Arrays get one element.** Enough to show the shape, not enough to test pagination.
