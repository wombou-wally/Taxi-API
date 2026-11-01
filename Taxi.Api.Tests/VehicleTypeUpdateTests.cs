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

public class VehicleTypeUpdateTests : IClassFixture<TaxiApiFactory>
{
    private const string Password = "Password123!";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public VehicleTypeUpdateTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetTokenForRoleAsync(Role role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@test.com";

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

    // Creates a fresh vehicle type with a unique name so tests never touch the seeded rows.
    private async Task<int> CreateVehicleTypeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var type = new VehicleType
        {
            Name = $"T{Guid.NewGuid():N}",
            Seats = 4,
            BaseFare = 4.00m,
            PricePerKm = 1.80m,
            PricePerMinute = 0.35m,
            MinimumFare = 7.00m,
            IsActive = true
        };
        context.VehicleTypes.Add(type);
        await context.SaveChangesAsync();
        return type.Id;
    }

    private async Task<VehicleType> GetVehicleTypeAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.VehicleTypes.AsNoTracking().SingleAsync(v => v.Id == id);
    }

    private async Task<HttpResponseMessage> PutAsync(int id, object body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"api/vehicle-types/{id}")
        {
            Content = JsonContent.Create(body)
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    private static object ValidBody() => new
    {
        BaseFare = 5.00m,
        PricePerKm = 2.345m,
        PricePerMinute = 0.40m,
        MinimumFare = 8.00m,
        IsActive = false
    };

    [Fact]
    public async Task Update_NoToken_Returns401()
    {
        var id = await CreateVehicleTypeAsync();

        var response = await PutAsync(id, ValidBody(), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_CustomerToken_Returns403()
    {
        var id = await CreateVehicleTypeAsync();
        var token = await GetTokenForRoleAsync(Role.Customer);

        var response = await PutAsync(id, ValidBody(), token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(4.00m, (await GetVehicleTypeAsync(id)).BaseFare);
    }

    [Fact]
    public async Task Update_AdminToken_Returns200_AndSavesRoundedValues()
    {
        var id = await CreateVehicleTypeAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await PutAsync(id, ValidBody(), token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = await GetVehicleTypeAsync(id);
        Assert.Equal(5.00m, saved.BaseFare);
        Assert.Equal(2.35m, saved.PricePerKm);       // 2.345 rounded to 2 decimals
        Assert.Equal(0.40m, saved.PricePerMinute);
        Assert.Equal(8.00m, saved.MinimumFare);
        Assert.False(saved.IsActive);
        Assert.Equal(4, saved.Seats);                // not editable
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var token = await GetTokenForRoleAsync(Role.Admin);

        var response = await PutAsync(int.MaxValue, ValidBody(), token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_NegativePrice_Returns400_AndNothingChanges()
    {
        var id = await CreateVehicleTypeAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var body = new { BaseFare = 5.00m, PricePerKm = -1.00m, PricePerMinute = 0.40m, MinimumFare = 8.00m, IsActive = true };
        var response = await PutAsync(id, body, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1.80m, (await GetVehicleTypeAsync(id)).PricePerKm);
    }

    [Fact]
    public async Task Update_ZeroPrice_Returns400()
    {
        var id = await CreateVehicleTypeAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var body = new { BaseFare = 0m, PricePerKm = 2.00m, PricePerMinute = 0.40m, MinimumFare = 8.00m, IsActive = true };
        var response = await PutAsync(id, body, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_MinimumBelowBase_Returns400()
    {
        var id = await CreateVehicleTypeAsync();
        var token = await GetTokenForRoleAsync(Role.Admin);

        var body = new { BaseFare = 9.00m, PricePerKm = 2.00m, PricePerMinute = 0.40m, MinimumFare = 5.00m, IsActive = true };
        var response = await PutAsync(id, body, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(4.00m, (await GetVehicleTypeAsync(id)).BaseFare);
    }
}