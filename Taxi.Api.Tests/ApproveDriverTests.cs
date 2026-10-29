using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class ApproveDriverTests : IClassFixture<TaxiApiFactory>
{
    private const string Password = "Password123!";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public ApproveDriverTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewEmail(string prefix) => $"{prefix}_{Guid.NewGuid():N}@test.com";

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

    // Registers a driver through the API and returns the new user's id (read from the database).
    private async Task<int> RegisterDriverAsync()
    {
        var email = NewEmail("driver");

        var response = await _client.PostAsJsonAsync("api/drivers/register", new
        {
            Email = email,
            Password,
            FullName = "Test Driver",
            Phone = "5065550101",
            LicenceNumber = "LIC-55555"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var user = await context.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        return user.Id;
    }

    private async Task<ApprovalStatus> GetStatusAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var profile = await context.DriverProfiles.AsNoTracking().SingleAsync(d => d.UserId == userId);
        return profile.Status;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Approve_NoToken_Returns401()
    {
        var userId = await RegisterDriverAsync();

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApprovalStatus.Pending, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Approve_CustomerToken_Returns403()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Customer);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApprovalStatus.Pending, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Approve_AdminToken_Returns200_AndStatusBecomesApproved()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApprovalStatus.Approved, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Approve_UnknownId_Returns404()
    {
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{int.MaxValue}", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Approve_Twice_SecondReturns409()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var first = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Approve_RemovesDriverFromPendingList()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var before = await SendAsync(HttpMethod.Get, "api/drivers/pending", token);
        var beforeList = await before.Content.ReadFromJsonAsync<List<PendingDriverResponse>>();
        Assert.NotNull(beforeList);
        Assert.Contains(beforeList, d => d.UserId == userId);

        var approve = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var after = await SendAsync(HttpMethod.Get, "api/drivers/pending", token);
        var afterList = await after.Content.ReadFromJsonAsync<List<PendingDriverResponse>>();
        Assert.NotNull(afterList);
        Assert.DoesNotContain(afterList, d => d.UserId == userId);
    }

    [Fact]
    public async Task Reject_NoToken_Returns401()
    {
        var userId = await RegisterDriverAsync();

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApprovalStatus.Pending, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Reject_CustomerToken_Returns403()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Customer);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ApprovalStatus.Pending, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Reject_AdminToken_Returns200_AndStatusBecomesRejected()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApprovalStatus.Rejected, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Reject_UnknownId_Returns404()
    {
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{int.MaxValue}", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reject_Twice_SecondReturns409()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var first = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Reject_AlreadyApproved_Returns409_AndStatusStaysApproved()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var approve = await SendAsync(HttpMethod.Post, $"api/drivers/approve/{userId}", token);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var reject = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal(ApprovalStatus.Approved, await GetStatusAsync(userId));
    }

    [Fact]
    public async Task Reject_RemovesDriverFromPendingList()
    {
        var userId = await RegisterDriverAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var reject = await SendAsync(HttpMethod.Post, $"api/drivers/reject/{userId}", token);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var after = await SendAsync(HttpMethod.Get, "api/drivers/pending", token);
        var list = await after.Content.ReadFromJsonAsync<List<PendingDriverResponse>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, d => d.UserId == userId);
    }
}