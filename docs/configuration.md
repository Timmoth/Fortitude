# Configuration

Every setting the Fortitude server understands, in one place.

## Ports

Ports are read from the environment, not the `Settings` section, because they have to be known
before the host is built.

| Variable | Example | Description |
|---|---|---|
| `PORT` | `5005` | Bind a single port. |
| `PORTS` | `5000-5005`, `8080,8082`, `5000-5002,9000` | Bind several, as a list and/or ranges. |

If neither is set, Kestrel's usual configuration applies (`ASPNETCORE_URLS`, `launchSettings.json`).

Each connecting client reserves one of the bound ports and only receives requests that arrive
on it. That is what keeps concurrent tests isolated, so give the server at least as many ports
as you expect simultaneous clients:

```bash
docker run -e PORTS=5000-5005 -p 5000-5005:5000-5005 aptacode/fortitude-server:latest
```

A client that cannot reserve a port still connects, but becomes *unrouted*: it receives
requests arriving on any port no client owns, sharing them with every other unrouted client.
The server logs a warning when this happens.

## Settings

These bind from the `Settings` configuration section, so they can come from `appsettings.json`,
command line arguments, or environment variables. In Docker use the double-underscore form:

```bash
docker run -e PORT=5005 -e Settings__ResponseTimeoutSeconds=30 ... 
```

### `Settings:ResponseTimeoutSeconds`

**Default `5`.** How long the server waits for a connected client to produce a response before
returning `504 Gateway Timeout`.

Raise it when a handler does real work, when you use `WithDelay` for longer than this, or when
you want to step through a responder in a debugger without the request expiring underneath
you. Values of zero or less fall back to the default.

### `Settings:Broadcast`

**Default `false`.** Forces every connected client to receive every request, bypassing port
routing entirely.

This disables isolation, so only use it with a single connected client. It exists for setups
where the client cannot control which port the system under test calls - .NET Aspire, for
instance, hands out a single endpoint through service discovery.

### `Settings:ConfigDirectory`

**Default: `config` beside the executable.** Where YAML handler definitions are read from.

Searched recursively; both `.yaml` and `.yml` are recognised. Relative paths resolve against
the application directory. The default is the path the Docker image expects a volume at:

```bash
docker run -e PORT=5005 -p 5005:5005 -v ./config:/app/config aptacode/fortitude-server:latest
```

### `Settings:HotReload`

**Default `true`.** Watches the handler directory and reloads definitions when they change, so
you can edit handlers against a running server. Reloads are debounced, because editors
commonly produce several filesystem events for one save.

### `Settings:MaxMessageSizeBytes`

**Default `33554432` (32 MB).** The largest request or response body that can be relayed
between the server and a connected client.

Bodies travel over SignalR, which caps received messages at 32 KB unless told otherwise -
smaller than an ordinary JSON fixture, and the symptom is a gateway timeout rather than
anything that names the real cause.

### `Settings:EnableAdminUi`

**Default `true`.** Serves the live monitoring and YAML editing UI at `/fortitude`.

The UI is unauthenticated and can write handler files. Turn it off when the server is
reachable by anything you do not trust.

### `Settings:EnableAdminApi`

**Default `true`.** Serves the [admin API](./admin-api.md) at `/fortitude/api`.

Same caveat: unauthenticated, and it can write handler files.

## Always available

`GET /fortitude/health` returns `server running` regardless of the UI and API settings. Use it
as a container health check.

## Example: a locked-down server

```bash
docker run \
  -e PORTS=5000-5005 \
  -e Settings__EnableAdminUi=false \
  -e Settings__EnableAdminApi=false \
  -e Settings__HotReload=false \
  -p 5000-5005:5000-5005 \
  -v ./config:/app/config:ro \
  aptacode/fortitude-server:latest
```

Mocking still works; nothing can reconfigure it over the network, and the handler definitions
are read-only.
