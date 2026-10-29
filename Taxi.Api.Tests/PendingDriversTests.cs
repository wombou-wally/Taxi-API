using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class PendingDriversTests : IClassFixture<TaxiApiFactory>
{
    private const string Password = "Password123!";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public PendingDriversTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@test.com";

    // Creates a user with the given role directly in the test database, then logs in through the API.
    private async Task<string> GetTokenForRoleAsync(Role role)
    {
        var email = NewEmail(role.ToString().ToLowerInvariant());

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

            var user = new User
            {
                Email = email,
                FullName = $"Test {role}",
                Phone = "5065550100",
                Role = role,
                PasswordHash = ""
            };
            user.PasswordHash = hasher.HashPassword(user, Password);

            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("api/auth/login", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        return login.Token;
    }

    private async Task<HttpResponseMessage> GetPendingAsync(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/drivers/pending");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Pending_NoToken_Returns401()
    {
        var response = await GetPendingAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Pending_CustomerToken_Returns403()
    {
        var token = await GetTokenForRoleAsync(Role.Customer);

        var response = await GetPendingAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pending_DriverToken_Returns403()
    {
        var token = await GetTokenForRoleAsync(Role.Driver);

        var response = await GetPendingAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pending_AdminToken_Returns200_AndContainsRegisteredDriver()
    {
        var adminToken = await GetTokenForRoleAsync(Role.Admin);

        var driverEmail = NewEmail("driver");
        var registerResponse = await _client.PostAsJsonAsync("api/drivers/register", new
        {
            Email = driverEmail,
            Password,
            FullName = "Pending Driver",
            Phone = "5065550101",
            LicenceNumber = "LIC-98765"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var response = await GetPendingAsync(adminToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var drivers = await response.Content.ReadFromJsonAsync<List<PendingDriverResponse>>();
        Assert.NotNull(drivers);

        var found = drivers.SingleOrDefault(d => d.Email == driverEmail);
        Assert.NotNull(found);
        Assert.Equal("LIC-98765", found.LicenceNumber);
    }
}