using Fortitude.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class FortitudeClientTests
{
    private readonly ITestOutputHelper _output;

    public FortitudeClientTests(ITestOutputHelper output)
    {
        _output = output;
    }
    public class FakeUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
    [Fact]
    public async Task Test1()
    {
        // Given
        var fortitude = new FortitudeClient(_output);
        await fortitude.StartAsync(url: "http://localhost:5093/fortitude");

        var fakeUser = new FakeUser();
        var handler = new FortitudeHandlerExtensions.FortitudeHandlerBuilder()
            .Get()
            .HttpRoute($"/users/{fakeUser.Id}")
            .Build(r =>  new FortitudeResponse(r.RequestId)
            {
                Body = System.Text.Json.JsonSerializer.Serialize(fakeUser)
            });

        fortitude.Add(handler);

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://localhost:5093/users/{fakeUser.Id}");

        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response: {body}");

        // Then
        await fortitude.StopAsync();
        var returnedUser = System.Text.Json.JsonSerializer.Deserialize<FakeUser>(body);
        Assert.Equal(fakeUser.Id, returnedUser?.Id);

    }

}