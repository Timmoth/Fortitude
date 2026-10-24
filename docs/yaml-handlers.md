# YAML defined Handlers
Fortitude can load and run static handlers defined in YAML.
This is ideal when you want a mock API that lives outside of test code — for example when:
- running the Fortitude Server in Docker
- supporting non-.NET services
- sharing mock APIs between teams
- using the Blazor UI to tweak behavior live

YAML handlers behave exactly like handlers defined in code, they can match on method, route, headers, query, and body, and they produce HTTP responses.

## Minimal YAML handler
Create a file called handlers.yaml:
```yaml
handlers:
  - match:
      methods: [GET]
      route: /health
    response:
      status: 200
      body:
        text: OK
```
This defines:
GET /health → 200 OK with plain-text “OK”

## JSON response example
```yaml
handlers:
  - match:
      methods: [GET]
      route: /users/{id}
    response:
      status: 200
      body:
        json:
          id: {{route.id}}
          name: Alice
          age: 30
```
Returns:
```json
{
  "id": 1,
  "name": "Alice",
  "age": 30
}
```

## Matching requests
Match by HTTP method and route
```yaml
match:
  methods: [POST]
  route: /users
```
Routes may include parameters using {} syntax:
```yaml
route: /users/{id}
```
Match headers
```yaml
match:
  headers:
    content-type: application/json
    x-api-key: secret
```
Match query parameters
```yaml
match:
  route: /search
  query:
    q: fortitude
    page: "2"
```
Match body (contains)
```yaml
match:
  methods: [POST]
  body:
    contains: alice@example.com
```
Match body (JSON predicate)
```yaml
match:
  methods: [POST]
  route: /users
  body:
    json: email == "alice@example.com"
```
The JSON expression is evaluated against the request body.
The handler matches only if the predicate evaluates to true.

## Responses
Returning headers
```yaml
response:
  status: 201
  headers:
    location: /users/123
    x-created-by: fortitude
  body:
    json:
      id: 123
      name: Alice
```

Handlers are evaluated in order, and the last matching handler wins.
```yaml
handlers:
  - match:
      route: /users/1
    response:
      status: 404

  - match:
      methods: [GET]
      route: /users/1
    response:
      status: 200
      body:
        json:
          id: 1
          name: Alice
```
Because the second handler is more specific and appears later, it overrides the first for GET requests.

## Response templating
Fortitude supports templated values in responses using {{ }} syntax.

Templates resolve against four sources - `route`, `body`, `query` and `headers` - and work
anywhere in the response body, including inside nested objects and arrays. Quoting is optional:
`id: {{route.id}}` and `id: "{{route.id}}"` behave identically.

```yaml
handlers:
  - match:
      methods: [POST]
      route: /users/{id}
    response:
      status: 200
      body:
        json:
          userId: "{{route.id}}"
          email: "{{body.email}}"
```
Request:
```
POST /users/42
{
  "email": "alice@example.com"
}
```
Response:

```
{
  "userId": "42",
  "email": "alice@example.com"
}
```
Templated text response
```
response:
  status: 200
  body:
    text: "Hello {{body.name}}"
```
Templated response headers
```
response:
  status: 200
  headers:
    x-request-id: "{{headers.x-request-id}}"
  body:
    text: OK
```

If a request header has multiple values, the first value is used for templating.

## Using YAML with Docker
Mount or pass your YAML file into the Fortitude Server container:
```bash
 docker run -p 8080:8080 -v ./config:/app/config aptacode/fortitude-server:latest
```
Fortitude will load and activate all handlers from the YAML file at startup.
## Editing handlers live via the web UI
When the Fortitude Server is running, open:
```
http://localhost:<port>/fortitude
```
From there you can:
- View all loaded YAML files
- Edit handlers live
- Add new YAML files
- Enable / disable handlers
- Apply changes instantly without restarting the server

## How values are typed

YAML gives Fortitude every scalar as text, so the JSON type is inferred when the response is
written:

| YAML | JSON |
|---|---|
| `count: 3` | `3` |
| `flag: true` | `true` |
| `name: Alice` | `"Alice"` |
| `userId: "{{route.id}}"` against `/users/42` | `42` |
| `postcode: "01234"` | `"01234"` |

Values with a redundant leading zero stay strings, because those are almost always identifiers,
postcodes or phone numbers that would be corrupted by being read as numbers. A template that
resolves to nothing renders as an empty string.

## Where definitions live

Handler files are read from `Settings:ConfigDirectory`, which defaults to a `config` directory
beside the executable - the path the Docker image expects a volume at:

```bash
docker run -e PORT=5005 -p 5005:5005 -v ./config:/app/config aptacode/fortitude-server:latest
```

The directory is searched recursively, and both `.yaml` and `.yml` are picked up. Files load in
path order, and since the last matching handler wins, a definition in `z.yaml` overrides one for
the same route in `a.yaml`.

`Settings:HotReload` (on by default) watches the directory and reloads when files change, so you
can edit definitions against a running server.

A file that fails to parse is reported and skipped rather than taking the server down with it -
its siblings still load, and the live UI shows the parse error against the file.

## Precedence over client handlers

YAML handlers are matched on the server, before a request is forwarded to any connected client.
If a YAML definition and a client-registered handler both match the same request, the YAML one
answers it and the client never sees the request.
