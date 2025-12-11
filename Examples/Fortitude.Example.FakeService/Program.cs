using Fortitude.Client;
using Fortitude.Example.Api;

var fortitudeBase = Environment.GetEnvironmentVariable("FORTITUDE_URL") ?? throw new ArgumentNullException("FORTITUDE_URL");

var (fortitude, url) = await FortitudeServer.ConnectAsync(fortitudeBase);

var users = new List<User>
{
    new User(998, "Alice", "alice@example.com")
};

fortitude.Accepts()
    .Post()
    .HttpRoute("/users")
    .Returns((req, res) =>
    {
        var incoming = req.Body.ToJson<User>()!;

        // Simulate creating a new user with an incrementing ID
        var newId = users.Any() ? users.Max(u => u.Id) + 1 : 1;

        var created = new User(newId, incoming.Name, incoming.Email);
        users.Add(created);

        res.Created(created);
    });

fortitude.Accepts()
    .Get()
    .HttpRoute("/users")
    .Returns((_, res) => res.Ok(users));

fortitude.Accepts()
    .Get()
    .HttpRoute("/users/{id}")
    .Returns((req, res) =>
    {
        var id = req.Route.GetRouteParameter("/users/{id}", "id")?.ToString();
        var user = users.FirstOrDefault(u => u.Id.ToString() == id);

        if (user is null)
            res.NotFound();
        else
            res.Ok(user);
    });

fortitude.Accepts()
    .Put()
    .HttpRoute("/users/{id}")
    .Returns((req, res) =>
    {
        var id = req.Route.GetRouteParameter("/users/{id}", "id")?.ToString();
        var existing = users.FirstOrDefault(u => u.Id.ToString() == id);

        if (existing is null)
        {
            res.NotFound();
            return;
        }

        var incoming = req.Body.ToJson<User>()!;

        // Update in-place
        existing.Name = incoming.Name;
        existing.Email = incoming.Email;

        res.Ok(existing);
    });

fortitude.Accepts()
    .Delete()
    .HttpRoute("/users/{id}")
    .Returns((req, res) =>
    {
        var id = req.Route.GetRouteParameter("/users/{id}", "id")?.ToString();
        var existing = users.FirstOrDefault(u => u.Id.ToString() == id);

        if (existing is null)
        {
            res.NotFound();
            return;
        }

        users.Remove(existing);
        res.NoContent();
    });
