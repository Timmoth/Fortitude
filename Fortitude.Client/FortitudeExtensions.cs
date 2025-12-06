using System.Text.Json;
using Xunit.Abstractions;

namespace Fortitude.Client;

public static class FortitudeExtensions
{
    public static T? ToJson<T>(this byte[]? data, JsonSerializerOptions? options = null)
    {
        if(data == null) return default;

        return JsonSerializer.Deserialize<T>(data, options ?? JsonSerializerOptions.Web);
    }

    public static async Task<FortitudeClient> CreateAsync(string fortitudeBaseUrl, ITestOutputHelper logger)
    {
        var fortitude = new FortitudeClient(logger);
        await fortitude.StartAsync($"{fortitudeBaseUrl}/fortitude");
        return fortitude;
    }
}

public static class FortitudeServer
{
    public static async Task<FortitudeClient> ConnectAsync(string fortitudeBaseUrl, ITestOutputHelper logger)
    {
        var fortitude = new FortitudeClient(logger);
        await fortitude.StartAsync($"{fortitudeBaseUrl}/fortitude");
        return fortitude;
    }
}