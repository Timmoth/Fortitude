# Installation

Fortitude ships two things:

| Artefact | Where | Used for |
|---|---|---|
| `Fortitude` NuGet package | [nuget.org](https://www.nuget.org/packages/Fortitude) | The library your tests reference. |
| `ghcr.io/timmoth/fortitude` image | [GHCR](https://github.com/Timmoth/Fortitude/pkgs/container/fortitude) | The standalone mock server. |

Both are public and neither needs a credential to consume.

---

## The package

```bash
dotnet add package Fortitude
```

That is the whole of it - nuget.org is configured out of the box, so there is no feed to add
and no token to create. The package targets `net10.0`.

> Versions `10.1.4` and `10.2.0` were published to GitHub Packages during a brief move off
> nuget.org. Publishing returned to nuget.org afterwards, so install from there; those two
> versions are the same code and are not worth chasing.

### Behind a private feed

If your organisation mirrors nuget.org through Azure Artifacts, JFrog or similar, Fortitude
needs nothing special - it is an ordinary package. Point at your mirror the way you would for
any other dependency, and make sure `Fortitude` is allowed through if the mirror uses an
allowlist.

---

## The server image

The library can mock HTTP entirely in-process, in which case you need nothing below. The
server is for the cases where the system under test has to make a real network call - see
[mocking-apis](./mocking.md) for which mode fits.

```bash
# Pull
docker pull ghcr.io/timmoth/fortitude:latest

# A single fixed port
docker run -e PORT=5005 -p 5005:5005 ghcr.io/timmoth/fortitude:latest

# A port range - each connecting client reserves one, which is what keeps
# parallel tests isolated from each other
docker run -e PORTS=5000-5005 -p 5000-5005:5000-5005 ghcr.io/timmoth/fortitude:latest

# With YAML handler definitions mounted in
docker run -e PORT=5005 -p 5005:5005 -v ./config:/app/config ghcr.io/timmoth/fortitude:latest
```

The image is public, so `docker pull` needs no login. Pulling a specific release is
`ghcr.io/timmoth/fortitude:v10.2.0`; `linux/amd64` and `linux/arm64` are both built, and the
container runs as a non-root user.

Confirm it is up:

```bash
curl http://localhost:5005/fortitude/health   # -> server running
```

Then open `http://localhost:5005/fortitude` for the live request monitor.

Every `PORT`, `PORTS` and `Settings:*` value is listed in [configuration](./configuration.md).

### Without Docker

```bash
git clone https://github.com/Timmoth/Fortitude.git
cd Fortitude
dotnet run --project ./Fortitude.Server
```

See [local-development](./local-development.md) for working on Fortitude itself, and
[aspire-integration-tests](./aspire-integration-tests.md) or the `Examples/` directory for
starting the server from a test run rather than by hand.

---

## Troubleshooting

**`Fortitude` is not found by `dotnet add package`.** Check that nuget.org is actually among
your sources - a `nuget.config` in the repository or a parent directory may have cleared it:

```bash
dotnet nuget list source
```

**An old version is resolved.** A `packageSourceMapping`, or a pinned version in
`Directory.Packages.props`, will hold it back. `dotnet list package --outdated` shows what is
available.

**`docker pull` returns 401 or 404.** The image is public, so this should not happen. A new
GHCR package starts private by default, so if a future release publishes under a new name the
owner has to change its visibility once from its
[package settings page](https://github.com/Timmoth/Fortitude/pkgs/container/fortitude).

**The server starts but a test gets `503 No Fortitude client is connected`.** The request
arrived on a port no client had reserved. [architecture](./architecture.md) explains the
routing, and `Settings:Broadcast` in [configuration](./configuration.md) is the escape hatch
for when a single client should see everything.
