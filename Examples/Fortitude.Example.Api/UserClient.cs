using System.Net;

namespace Fortitude.Example.Api;

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

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<User>();
    }

    public async Task<User> CreateAsync(User user)
    {
        var response = await http.PostAsJsonAsync("users", user);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<User>())!;
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