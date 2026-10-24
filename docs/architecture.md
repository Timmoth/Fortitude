# Architecture

How a request becomes a response, and where to add things.

## The projects

| Project | What it is |
|---|---|
| `Fortitude.Client` | The `Fortitude` NuGet package. Handler matching, the fluent builder, request and response models, the YAML loader, the OpenAPI importer, the proxy, and the in-process interceptor. |
| `Fortitude.Server` | The standalone server and its Docker image. Catch-all middleware, the SignalR hub, port reservation, the YAML handler store, the live UI and admin API. |

`Fortitude.Server` depends on `Fortitude.Client`, never the reverse. Anything a test author
needs at runtime belongs in the client - which is why handler matching lives there even though
the server also uses it for YAML handlers.

## The three modes

### Intercept mode

No server. `AddFortitudeClient` installs an `IHttpMessageHandlerBuilderFilter` that appends a
`DelegatingHandler` to every `HttpClient` the container builds. Outgoing calls are answered
from the handler set without touching a socket.

```
[Test] -> registers handlers -> [FortitudeClient]
                                      ^
[SUT] -> HttpClient.SendAsync --------+   (never leaves the process)
```

Fastest, and the only mode with no network involved. It cannot test anything that depends on
real HTTP behaviour, and it only intercepts clients built by the container.

### Server mode

The server runs separately. The system under test is pointed at it and makes genuine HTTP
calls, which the server relays to the test over SignalR.

```
[Test] --1. connect, reserve port--> [Server] 
       <-3. IncomingRequest---------
       --4. SubmitResponse--------->
                                        ^
[SUT]  --2. real HTTP on that port -----+
       <-5. real HTTP response ---------
```

True black-box testing: the system under test is unmodified and genuinely talks HTTP. The
cost is a round trip per request and a timeout to consider.

### YAML mode

Handler definitions live as files in the server's config directory. They are matched *before*
anything is forwarded to a client, so no test process is needed at all. This is what makes the
server useful for local development and for non-.NET consumers.

## Request lifecycle, server mode

1. A request arrives on one of Kestrel's bound ports and hits `FortitudeMiddleware`, a
   catch-all. Anything under `/fortitude` is passed through to the UI, hub, health endpoint
   and admin API instead.
2. The request is captured as a `FortitudeRequest` and recorded in `RequestTracker` for the
   live UI.
3. `YamlHandlerStore` is asked first. If a YAML handler matches, it answers and the request
   goes no further.
4. Otherwise `ConnectedClientService` is asked which client reserved the port the request
   arrived on. If one did, the request goes to that client alone. If nobody owns the port, it
   goes to the unrouted clients. If there are none, the response is `503`.
5. `PendingRequestStore` parks the request against its id and waits, up to
   `Settings:ResponseTimeoutSeconds`.
6. The client matches the request against its handlers, runs the responder, and calls
   `SubmitResponse` on the hub.
7. The hub completes the parked request and the middleware writes the response out.

A timeout at step 5 produces `504`; no matching handler on the client produces `501` and is
recorded in `UnmatchedRequests`.

## Handler matching

`FortitudeHandler.SelectFor` is the single place precedence is decided, used by the client,
the intercept path and the YAML store alike. It walks candidates in reverse registration order
and keeps the highest priority, so equal priorities resolve to the most recently registered.

A handler declines to match if it is disabled, has exhausted its `Times(n)` allowance, or is
bound to a scenario that is in a different state. That is what lets an exhausted handler step
aside for a fallback registered earlier.

## Concurrency

A few places carry state across threads, and all of them follow the same pattern: writers take
a lock and swap an immutable snapshot, readers take the snapshot.

- `FortitudeClient` keeps handlers in a list guarded by a lock, with a `volatile` array that
  dispatch reads.
- `YamlHandlerStore` replaces its handler list wholesale on reload rather than mutating it, so
  a reload during traffic cannot be observed half-applied.
- `RequestTracker` guards its queue and response map with one lock, so the two cannot drift.
- `PendingRequestStore` uses a `ConcurrentDictionary` of `TaskCompletionSource`.

`Times(n)` is deliberately best-effort under genuine concurrency: two requests can both pass
the check before either is counted. Making it exact would mean holding a lock across the
responder, which is a worse trade for a test tool.

## Adding a feature

**A new matcher** goes on `FortitudeHandlerBuilder` and, if it should be expressible in YAML,
on `MatchDefinition` plus `FortitudeYamlLoader.ToHandler`.

**A new response option** goes on `FortitudeResponse`, and on `ResponseDefinition` for YAML.
If it needs server-side behaviour - as faults do, since a connection reset cannot be
represented as a status code - it also needs handling in `FortitudeMiddleware.WriteResponse`
and in `FortitudeResponse.ToHttpResponseMessage` for intercept mode. Those two are the pair to
keep in step.

**A new setting** goes on `Settings` in `Program.cs`, and in the README's configuration table
and `docs/configuration.md`.

Anything that depends on ports or real HTTP needs a test built on `LiveServer`, which starts a
real server on ephemeral ports. The in-memory `WebApplicationFactory` binds none, so it cannot
tell routed delivery from broadcast and will pass either way.
