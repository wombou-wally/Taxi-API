using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public record TestUser(int Id, string Email, string Token);

public static class TestHelpers
{
    public const string Password = "Password123!";

    // Creates a user with the given role directly in the test database, then logs in through the API.
    // Drivers also get a DriverProfile.
    public static async Task<TestUser> CreateUserAsync(TaxiApiFactory factory, HttpClient client, Role role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@test.com";
        int id;

        using (var scope = factory.Services.CreateScope())
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
            if (role == Role.Driver)
            {
                user.DriverProfile = new DriverProfile { LicenceNumber = "LIC-TEST" };
            }
            user.PasswordHash = hasher.HashPassword(user, Password);

            context.Users.Add(user);
            await context.SaveChangesAsync();
            id = user.Id;
        }

        var loginResponse = await client.PostAsJsonAsync("api/auth/login", new { Email = email, Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        return new TestUser(id, email, login.Token);
    }

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await client.SendAsync(request);
    }
}