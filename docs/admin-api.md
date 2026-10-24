# Admin API

Fortitude's handlers can be configured from .NET, or by mounting a directory of YAML files.
Neither helps if your tests are written in JavaScript, Python, Java or Go - which is what the
admin API is for. Everything the live UI does is available over HTTP, so any language can drive
the mock server.

It is served at `/fortitude/api` and is on by default. Turn it off with
`Settings:EnableAdminApi=false`.

> The API is unauthenticated and can write handler files, exactly like the live UI. Disable it
> when the server is reachable by anything you do not trust.

## Handlers

### `GET /fortitude/api/handlers`

Lists the handler definition files currently loaded.

```json
{
  "directory": "/app/config",
  "files": [
    { "fileName": "users.yaml", "handlers": 3, "error": null },
    { "fileName": "broken.yaml", "handlers": 0, "error": "..." }
  ]
}
```

A file that failed to parse appears with its error rather than being silently omitted.

### `GET /fortitude/api/handlers/{fileName}`

Returns the raw YAML of one file, or `404` if there is no such file.

### `PUT /fortitude/api/handlers/{fileName}`

Creates or replaces a file. The body is the YAML itself. Handlers go live immediately - there is
no restart and no reload call needed.

```bash
curl -X PUT http://localhost:5005/fortitude/api/handlers/users.yaml \
  --data-binary @users.yaml
```

| Status | Meaning |
|---|---|
| `200` | Saved, with the number of handlers it defines |
| `400` | The file name was rejected (see below) |
| `422` | Saved, but the YAML does not parse - the message says why |

File names must be a bare name ending in `.yaml` or `.yml`. Anything with a path component,
percent-encoding, or characters invalid in a file name is refused, so the API cannot be used to
write outside the handler directory.

### `DELETE /fortitude/api/handlers/{fileName}`

Removes a file and reloads. Returns `204`, including when the file was already gone.

### `POST /fortitude/api/handlers/reload`

Re-reads the directory. Only needed when `Settings:HotReload` is off, or when files arrived by
some route the watcher cannot see.

## Traffic

### `GET /fortitude/api/requests?limit=50`

The most recent requests, newest first, with the response each received.

```json
{
  "total": 1402,
  "retained": 1000,
  "requests": [
    {
      "id": "…",
      "time": "2026-10-24T14:30:00Z",
      "method": "POST",
      "route": "/orders",
      "query": "",
      "headers": { "content-type": ["application/json"] },
      "body": "{\"item\":\"widget\"}",
      "response": { "status": 201, "contentType": "application/json" }
    }
  ]
}
```

`total` counts everything seen since start; `retained` is how many are still buffered.

### `DELETE /fortitude/api/requests`

Clears the buffer. Useful between test cases. Does not reset `total`.

## Clients

### `GET /fortitude/api/clients`

The Fortitude clients connected over SignalR and the port each reserved. A port of `-1` means
the client is unrouted and will receive traffic for any port no client owns.

```json
{
  "count": 2,
  "clients": [
    { "connectionId": "…", "port": 5001 },
    { "connectionId": "…", "port": 5002 }
  ]
}
```

## Health

### `GET /fortitude/health`

Returns `server running`. Always available, even with the API and UI disabled - use it as a
container health check.
