using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class DriverRegistrationTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public DriverRegistrationTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewEmail() => $"driver_{Guid.NewGuid():N}@test.com";

    private static object ValidBody(string email) => new
    {
        Email = email,
        Password = "Password123!",
        FullName = "Test Driver",
        Phone = "5065550100",
        LicenceNumber = "LIC-12345"
    };

    [Fact]
    public async Task RegisterDriver_Valid_Returns201()
    {
        var response = await _client.PostAsJsonAsync("api/drivers/register", ValidBody(NewEmail()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDriver_DuplicateEmail_Returns409()
    {
        var email = NewEmail();

        var first = await _client.PostAsJsonAsync("api/drivers/register", ValidBody(email));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("api/drivers/register", ValidBody(email));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RegisterDriver_MissingLicence_Returns400()
    {
        var body = new
        {
            Email = NewEmail(),
            Password = "Password123!",
            FullName = "Test Driver",
            Phone = "5065550100",
            LicenceNumber = ""
        };

        var response = await _client.PostAsJsonAsync("api/drivers/register", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDriver_InvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("api/drivers/register", ValidBody("not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDriver_ShortPassword_Returns400()
    {
        var body = new
        {
            Email = NewEmail(),
            Password = "short",
            FullName = "Test Driver",
            Phone = "5065550100",
            LicenceNumber = "LIC-12345"
        };

        var response = await _client.PostAsJsonAsync("api/drivers/register", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDriver_Valid_CreatesDriverUserAndPendingProfile()
    {
        var email = NewEmail();

        var response = await _client.PostAsJsonAsync("api/drivers/register", ValidBody(email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var user = await context.Users
            .Include(u => u.DriverProfile)
            .FirstOrDefaultAsync(u => u.Email == email);

        Assert.NotNull(user);
        Assert.Equal(Role.Driver, user.Role);
        Assert.NotNull(user.DriverProfile);
        Assert.Equal(ApprovalStatus.Pending, user.DriverProfile.Status);
        Assert.Equal("LIC-12345", user.DriverProfile.LicenceNumber);
    }
}