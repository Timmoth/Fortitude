# Recording a real API

Writing a fixture by hand means guessing what the real service returns. Proxy mode removes the
guesswork: point Fortitude at the real dependency once, exercise your system, and keep what
comes back as a handler definition.

```csharp
fortitude.Accepts()
    .RouteStartsWith("/")
    .ProxiesTo("https://api.example.com", recordTo: "./fixtures");
```

Every matched request is forwarded upstream, the real response is returned to your system
under test, and the exchange is written to `./fixtures` as YAML. From then on, mount that
directory as the handler directory and the tests run with the real service out of the picture.

## Proxying without recording

Leave `recordTo` out to forward without writing anything - useful for running a local service
against a real dependency while watching the traffic in the live UI.

```csharp
fortitude.Accepts().RouteStartsWith("/api").ProxiesTo("https://api.example.com");
```

Proxying composes with everything else, so you can record most of an API while overriding the
parts you need to control:

```csharp
// Everything proxies to the real service...
fortitude.Accepts().RouteStartsWith("/").Priority(-100)
    .ProxiesTo("https://api.example.com", recordTo: "./fixtures");

// ...except the endpoint this test is actually about.
fortitude.Accepts().Get().HttpRoute("/users/42")
    .Returns((req, res) => res.NotFound());
```

## What gets written

One file per distinct call, named after the method and route - `get-users-7.yaml`. Replaying
the same call overwrites its own recording rather than piling up duplicates.

The match is deliberately narrow: method, route and query string. Request headers are not
matched on, because they vary far more between runs than they distinguish one call from
another.

JSON responses are written as structured YAML rather than an escaped blob, so the fixture stays
readable and editable:

```yaml
handlers:
  - match:
      methods:
      - GET
      route: /users/7
    response:
      status: 200
      headers:
        X-Request-Id: abc123
      body:
        json:
          id: "7"
          name: Alice
```

Note `id: "7"` is quoted. Strings that would otherwise read back as a number or boolean are
quoted on the way out, so a recording replays with the types the real service sent.

Anything that is not JSON is written as `text`.

## When the upstream is unreachable

A failed upstream call returns `502 Bad Gateway` with the reason, rather than a generic `500` -
the mock is fine, the thing behind it is not. Those exchanges are not recorded: a 502 from an
unreachable service is not a fixture anyone wants to replay.

## Limitations

- **Responses only.** Request bodies are forwarded but not recorded, so a generated handler
  matches on method, route and query alone.
- **Credentials are forwarded as-is.** If the real API needs an `Authorization` header, your
  system under test must supply it; it is passed straight through. It is not written into the
  recording, but check fixtures before committing them - a response body can carry secrets of
  its own.
- **No redirect following.** A `301` or `302` is returned to the caller rather than followed.
