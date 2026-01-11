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
          id: 1
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