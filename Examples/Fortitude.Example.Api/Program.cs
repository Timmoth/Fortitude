using Microsoft.Extensions.Options;

namespace Fortitude.Example.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder);

        var app = builder.Build();

        ConfigureMiddleware(app);
        ConfigureEndpoints(app);

        app.Run();
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi();

// Bind configuration section into a typed settings object
        builder.Services.Configure<ExternalApiSettings>(
            builder.Configuration.GetSection("ExternalApi"));

// Register HttpClient with BaseAddress from config
        builder.Services.AddHttpClient<IUserClient, UserClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ExternalApiSettings>>().Value;
            Console.WriteLine(settings.BaseUrl);

            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                throw new InvalidOperationException("External API BaseUrl is not configured.");

            client.BaseAddress = new Uri(settings.BaseUrl);
        });
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
    }

    private static void ConfigureEndpoints(WebApplication app)
    {
        // ---------- CRUD ENDPOINTS ----------

        app.MapGet("/users", async (IUserClient client) =>
            Results.Ok(await client.GetAllAsync()));

        app.MapGet("/users/{id}", async (int id, IUserClient client) =>
        {
            var user = await client.GetByIdAsync(id);
            return user is not null ? Results.Ok(user) : Results.NotFound();
        });

        app.MapPost("/users", async (User user, IUserClient client) =>
        {
            var created = await client.CreateAsync(user);
            return Results.Created($"/users/{created.Id}", created);
        });

        app.MapPut("/users/{id}", async (int id, User user, IUserClient client) =>
        {
            return await client.UpdateAsync(id, user)
                ? Results.NoContent()
                : Results.NotFound();
        });

        app.MapDelete("/users/{id}", async (int id, IUserClient client) =>
        {
            return await client.DeleteAsync(id)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }
}
public record User(int Id, string Name, string Email);

public interface IUserClient
{
    Task<IEnumerable<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task<User> CreateAsync(User user);
    Task<bool> UpdateAsync(int id, User user);
    Task<bool> DeleteAsync(int id);
}

public class ExternalApiSettings
{
    public string BaseUrl { get; set; } = "";
}