using System.Net;
using Fortitude.Example.Api;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Bind configuration section into a typed settings object
builder.Services.Configure<ExternalApiSettings>(
    builder.Configuration.GetSection("ExternalApi"));

// Register HttpClient with BaseAddress from config
builder.Services.AddHttpClient<IUserClient, UserClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ExternalApiSettings>>().Value;

    if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        throw new InvalidOperationException("External API BaseUrl is not configured.");

    client.BaseAddress = new Uri(settings.BaseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


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

app.Run();

namespace Fortitude.Example.Api
{
    // ---------- Models & Settings ----------

    public record User(int Id, string Name, string Email);

    public class ExternalApiSettings
    {
        public string BaseUrl { get; set; } = "";
    }


// ---------- External Service Client ----------

    public interface IUserClient
    {
        Task<IEnumerable<User>> GetAllAsync();
        Task<User?> GetByIdAsync(int id);
        Task<User> CreateAsync(User user);
        Task<bool> UpdateAsync(int id, User user);
        Task<bool> DeleteAsync(int id);
    }

    public class UserClient(HttpClient http) : IUserClient
    {
        public async Task<IEnumerable<User>> GetAllAsync() =>
            await http.GetFromJsonAsync<IEnumerable<User>>("users")
            ?? Enumerable.Empty<User>();

        public async Task<User?> GetByIdAsync(int id)
        {
            var response = await http.GetAsync($"users/{id}");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Request failed: {response.StatusCode} ({(int)response.StatusCode})");

            return await response.Content.ReadFromJsonAsync<User>();
        }

           

        public async Task<User> CreateAsync(User user)
        {
            var response = await http.PostAsJsonAsync("users", user);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<User>())
                   ?? throw new Exception("Failed to deserialize created user.");
        }

        public async Task<bool> UpdateAsync(int id, User user)
        {
            var response = await http.PutAsJsonAsync($"users/{id}", user);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var response = await http.DeleteAsync($"users/{id}");
            return response.IsSuccessStatusCode;
        }
    }
}