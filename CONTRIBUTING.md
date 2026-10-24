# Contributing to Fortitude

Thanks for taking an interest. Fortitude is a small project and contributions of any size are
welcome - a bug report with a reproduction is as useful as a pull request.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Docker is only needed for
the example integration tests.

```bash
git clone https://github.com/Timmoth/Fortitude.git
cd Fortitude

dotnet build Fortitude.sln
dotnet test Fortitude.Tests/Fortitude.Tests.csproj
```

The unit suite is fast - a few seconds - and runs with no external dependencies. It starts
real Kestrel servers on ephemeral ports, so it is safe to run in parallel with anything else.

The example suite is slower and exercises the real integrations:

```bash
dotnet test Examples/Fortitude.Example.Tests/Fortitude.Example.Tests.csproj
```

It runs anywhere. The Aspire tests use plain HTTP endpoints, so no trusted development
certificate is needed, and the container tests skip themselves with an explanation when no
Docker daemon is available. CI runs both suites.

To run the server locally and poke at the live UI:

```bash
PORTS=5000-5005 dotnet run --project ./Fortitude.Server/
# then open http://localhost:5000/fortitude
```

## The projects

| Project | What it is |
|---|---|
| `Fortitude.Client` | The `Fortitude` NuGet package. Handlers, matching, the YAML loader, the in-process interceptor. |
| `Fortitude.Server` | The standalone mock server and its Docker image. Middleware, SignalR hub, port reservation, live UI, admin API. |
| `Fortitude.Tests` | The test suite for both. |
| `Examples/` | A sample system under test plus Aspire and Docker integration tests. |

`Fortitude.Server` references `Fortitude.Client`, not the other way around. Anything a test
author needs at runtime belongs in the client.

## Making a change

1. **Branch from `main`.**
2. **Write a test first where you can.** Every behavioural change in this repo is covered by
   one; for a bug fix, a test that fails before your change is the most useful thing in the
   pull request.
3. **Keep the build clean.** It currently has zero warnings and should stay that way. Public
   API needs XML documentation - the package ships a docs file.
4. **Update the docs.** Behaviour that is not written down in `docs/` or the README does not
   really exist. A new matcher or response option should appear in the relevant guide.
5. **Run the full suite** before opening the pull request.

### Writing tests

- Name them for the behaviour, not the method: `APredicatedWait_IgnoresRequestsThatDoNotSatisfyIt`
  rather than `TestWaitForRequest2`.
- Prefer end-to-end. `LiveServer` in the test project starts a real server on ephemeral ports;
  use it for anything that depends on routing, ports or real HTTP. The in-memory
  `WebApplicationFactory` binds no ports and cannot distinguish routed from broadcast delivery.
- When fixing a bug, check the test fails without the fix. A test that passes either way is
  worse than no test, because it looks like coverage.
- Comment the *why* when a test exists to pin down something subtle, not the *what*.

## Dependencies

Package versions live in `Directory.Packages.props`, not in individual project files. NuGet
audit is enabled and high or critical advisories fail the build, so a vulnerable dependency
cannot be merged. Dependabot opens grouped update pull requests weekly.

## Releasing

Releases are automated. Pushing a tag of the form `v10.2.0` publishes the NuGet package and
the multi-arch Docker image; the tag is the source of the version number. Maintainers need
`NUGET_API_KEY`, `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` configured as repository secrets.

## Building a client for another language

The README has long asked for JS/TS, Python and Java clients, and the
[admin API](./docs/admin-api.md) is the way in: it exposes everything the live UI does over
plain HTTP, so a client in any language can configure handlers and read observed traffic
without touching SignalR. Open an issue if you are thinking about one - it would be good to
agree the shape first.

## Reporting bugs

Please include the Fortitude version, how you are running the server (Docker, `dotnet run`,
Aspire, or intercept mode), and ideally a failing test or a handler definition that reproduces
it. The live UI at `/fortitude` and the server logs usually show what was matched and what
was not.

For anything security-sensitive, see [SECURITY.md](./SECURITY.md) instead of opening an issue.
