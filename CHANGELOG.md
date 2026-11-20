# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/).

Versions before 10.2.0 predate this file; see the git history for those.

## [Unreleased]

### Added

- **Response delays.** `WithDelay` and `WithRandomDelay`, plus `delayMs` and `delay: {minMs, maxMs}`
  in YAML, for exercising timeouts, retries and circuit breakers.
- **Call limits and sequences.** `Times(n)`, `Once()`, `Twice()` retire a handler after a set
  number of calls so a later one takes over; `ReturnsInSequence` returns a different response
  each time, repeating the last. `CallCount` and `IsExhausted` expose the state.
- **Handler priority.** `Priority(n)` makes precedence explicit. The previous implicit
  last-registered-wins rule remains the default for equal priorities and is now documented.
- **Fault injection.** `ReturnsFault` simulates a connection reset, an empty body, or a
  response that claims to be JSON but is not. `FailsRandomly` fails a configurable proportion
  of calls.
- **Verification API.** `ShouldHaveReceived`, `ShouldHaveReceivedOnce`, `AtLeast`, `AtMost` and
  `ShouldNeverHaveBeenCalled` fail with the handler and the traffic it saw.
  `VerifyNoUnmatchedRequests` catches calls that matched nothing, listing both what arrived and
  what was registered.
- **Stateful scenarios.** `InScenario`/`WhenState`/`ThenState`, and `match.scenario` in YAML,
  for modelling multi-step flows.
- **OpenAPI import.** `LoadOpenApi` generates a handler per operation from an OpenAPI 3.x
  document in JSON or YAML, using spec examples where present and synthesising from schemas
  otherwise.
- **Proxy passthrough and recording.** `ProxiesTo` forwards to a real service and optionally
  writes each exchange out as a handler definition, so a recording can be replayed with the
  real service gone.
- **Admin REST API** at `/fortitude/api` for listing, reading, writing and deleting handler
  definitions and reading observed traffic, so the server can be driven from any language.
- **Template functions.** `{{uuid}}`, `{{now}}` with format variants, and `{{randomInt a b}}`.
- **File-backed response bodies.** `body.file` serves a fixture from disk, templated for text
  formats and verbatim for binary.
- **Configuration.** `Settings:ResponseTimeoutSeconds`, `ConfigDirectory`, `HotReload`,
  `MaxMessageSizeBytes`, `EnableAdminUi` and `EnableAdminApi`. All documented in the README.
- **Hot reload** of handler definitions, on by default.
- `.yml` files are recognised alongside `.yaml`, and handler directories are searched
  recursively in a deterministic order.
- The package now targets `net8.0`, `net9.0` and `net10.0`.
- Logging accepts any `Action<string>`, so NUnit, MSTest and TUnit users are no longer
  required to go through xUnit's `ITestOutputHelper`.
- **Binary response bodies in YAML.** `body.base64` serves bytes verbatim, with `contentType`
  alongside it. Record and replay now writes binary responses this way instead of decoding them
  as text, so a captured image replays as the image.
- `GET /fortitude/api/requests` reports a non-UTF-8 request body as `bodyBase64`.
- A test project covering the package on every framework it targets, run in CI and before
  release; previously only the `net10.0` build was ever executed.
- The Docker image is built and smoke tested on every pull request, and a `.dockerignore`
  keeps local build output out of the build context.
- CI, release automation and Dependabot.

### Changed

- **Published to GitHub Packages instead of nuget.org.** Installing now requires
  authenticating to the feed with a `read:packages` token even though the package is public;
  the README has the one-time setup. Symbols are embedded in the assembly rather than shipped
  as a `.snupkg`, which GitHub Packages does not support.
- **The package targets `net10.0` only.** It previously multi-targeted `net8.0` and `net9.0`
  to reach the then-current LTS; both reached end of life in 2026. Breaking for consumers
  still on those runtimes.
- Analyzers run at Recommended with warnings as errors, pinned to a fixed `AnalysisLevel` so
  the rule set does not change with the SDK in use. `.editorconfig` records what is disabled
  and why.
- The SDK is pinned to the .NET 10 band by `global.json`.
- Dependencies updated to the newest versions compatible with `net10.0`, including Aspire 17,
  whose `AddProject<TProject>` is replaced by `AddDotnetProject`.

- **`Settings:Broadcast` now defaults to `false`.** Per-client port routing - the basis of the
  parallel-execution claim - was off by default despite the documentation saying otherwise.
  Requests now go to the client that reserved the port they arrived on, falling back to
  unrouted clients only when nobody owns it, never to all clients.
- **Quoting in YAML now decides the JSON type.** `count: 3` is a number, `count: "3"` is a
  string. Previously both became numbers.
- Handler-supplied response headers now replace server-set ones rather than being skipped,
  and a content type is sent even with an empty body.
- The response timeout is configurable and no longer hard-coded to 5 seconds.
- Failing to reserve a port no longer kills the client's connection; the client connects as
  unrouted with a warning.
- A simulated delay now observes the caller's `CancellationToken`, so an `HttpClient.Timeout`
  or a cancelled request ends the call instead of waiting the delay out.
- Upstream proxy calls time out after 30 seconds rather than `HttpClient`'s 100 second default.
- The Docker image runs as a non-root user.

### Fixed

- **Unquoted `{{templates}}` never worked.** The form the README documented parses as a nested
  YAML mapping rather than text, so the example in the README returned a 500.
- **Templates below the first level were silently dropped**, because nested mappings
  deserialize to a different dictionary type than the top level.
- **Path traversal in the live UI.** A handler file name of `../../appsettings.json` escaped
  the config directory. File names are now validated.
- `WaitForRequestAsync` ignored its own predicate for requests arriving after the call, so any
  request satisfied a predicated wait. Each wait also leaked a `CancellationTokenSource`.
- The handler list was mutated while request threads iterated it, in both the client and the
  YAML store.
- A failed `StartAsync` left the client unusable, reporting "already started" on retry.
- Responses accumulated without bound in the request tracker, and `TotalRequests` reported the
  retained count rather than the total.
- Bodies larger than 32KB failed as a gateway timeout, SignalR's default message limit.
- A non-JSON body against a `body.json` matcher threw, surfacing as a 500 instead of simply
  not matching.
- The cURL importer missed `-H "Key: Value"`, the form browsers emit, and the JSON-to-YAML
  converter produced invalid YAML for any value containing a colon, `#`, newline or leading
  dash.
- A malformed handler file took down startup rather than being reported and skipped.
- Recorded and generated YAML lost string types: a JSON `"7"` replayed as the number `7`.
- `Microsoft.AspNetCore.SignalR.Common` 10.0.2 carried a known high-severity advisory
  (GHSA-334f-vvp4-7g6p).
- The test project had no test runner, so its entire suite had been silently skipped.
- **`ToCurl` produced a broken command for any body containing an apostrophe.** `\'` escapes
  nothing inside shell single quotes, so the rest of the command was reparsed as separate
  words; the generated cURL could not even be read back by the handler generator. Cookies were
  also emitted twice when the request carried them as both a header and parsed values.
- Binary bodies were decoded with a lenient UTF-8 decoder that substitutes replacement
  characters and never fails, so `ToCurl`, recorded handler definitions and the admin API all
  reported unreadable text where bytes should have been preserved.
- Recording a response was a linear scan of the retained request buffer, under the lock the
  live UI also uses.
- Every answered request held a timer open for the full response timeout.
- `PortReservationService` published its bound ports through a `HashSet` that was written under
  a lock and read without one.
- `Aspire.Hosting` 13.6.4 carried high and critical advisories (GHSA-f7vh-h5g3-7jrh,
  GHSA-4g86-54fm-h9h8) that failed a cold restore, and `Microsoft.AspNetCore.WebUtilities`
  2.3.12 arrived transitively with a moderate one (GHSA-6527-9fp8-r4x6).
