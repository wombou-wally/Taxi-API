using System.Net;
using System.Net.Http.Json;

namespace Taxi.Api.Tests;

public class AuthTests : IClassFixture<TaxiApiFactory>
{
    private readonly HttpClient _httpClient;
    public AuthTests(TaxiApiFactory factory)
    {
        _httpClient = factory.CreateClient();
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/register",
            new { email = "not-an-email", password = "147258369", fullName = "Wally", phone = "555" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var email = $"user-{Guid.NewGuid()}@test.com";
        var response = await _httpClient.PostAsJsonAsync("/api/auth/register",
            new { email, password = "458", fullName = "Wally", phone = "555" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        var email = $"user-{Guid.NewGuid()}@test.com";
        var password = "12345678";
        await _httpClient.PostAsJsonAsync("/api/auth/register",
            new { email, password, fullName = "Wally", phone = "555" });

        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = $"user-{Guid.NewGuid()}@test.com";
        await _httpClient.PostAsJsonAsync("/api/auth/register",
            new { email, password = "12345678", fullName = "Wally", phone = "555" });

        var response = await _httpClient.PostAsJsonAsync("/api/auth/login",
            new { email, password = "wrong-password-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_NoToken_Returns401()
    {
        var response = await _httpClient.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
