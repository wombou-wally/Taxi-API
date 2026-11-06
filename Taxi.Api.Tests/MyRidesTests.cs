using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class MyRidesTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public MyRidesTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<int> InsertRideAsync(
        int customerId, RideStatus status, int? driverId = null, DateTime? requestedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var ride = new Ride
        {
            CustomerId = customerId,
            DriverProfileUserId = driverId,
            VehicleTypeId = 1,
            Status = status,
            PickupLatitude = 47.0267,
            PickupLongitude = -65.4631,
            DropoffLatitude = 47.0400,
            DropoffLongitude = -65.5000,
            EstimatedFare = 14.20m,
            RequestedAt = requestedAt ?? DateTime.UtcNow
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return ride.Id;
    }

    private async Task<HttpResponseMessage> GetRidesAsync(string? token, string query = "")
    {
        return await TestHelpers.SendAsync(_client, HttpMethod.Get, "/api/rides" + query, token);
    }

    private async Task<List<int>> GetIdsAsync(string token, string query = "")
    {
        var response = await GetRidesAsync(token, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();
    }

    [Fact]
    public async Task NoToken_Gives401()
    {
        var response = await GetRidesAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerWithNoRides_Gets200AndEmptyList()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var ids = await GetIdsAsync(customer.Token);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task Customer_SeesOnlyOwnRides()
    {
        var a = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var b = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideA = await InsertRideAsync(a.Id, RideStatus.Requested);
        var rideB = await InsertRideAsync(b.Id, RideStatus.Requested);

        var idsA = await GetIdsAsync(a.Token);
        var idsB = await GetIdsAsync(b.Token);

        Assert.Equal(new[] { rideA }, idsA);
        Assert.Equal(new[] { rideB }, idsB);
    }

    [Fact]
    public async Task Rides_AreReturnedNewestFirst()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var older = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: DateTime.UtcNow.AddHours(-2));
        var newer = await InsertRideAsync(customer.Id, RideStatus.Cancelled, requestedAt: DateTime.UtcNow.AddHours(-1));

        var ids = await GetIdsAsync(customer.Token);

        Assert.Equal(new[] { newer, older }, ids);
    }

    [Fact]
    public async Task Limit_ReturnsOnlyTheNewestRides()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: DateTime.UtcNow.AddHours(-3));
        var middle = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: DateTime.UtcNow.AddHours(-2));
        var newest = await InsertRideAsync(customer.Id, RideStatus.Cancelled, requestedAt: DateTime.UtcNow.AddHours(-1));

        var ids = await GetIdsAsync(customer.Token, "?limit=2");

        Assert.Equal(new[] { newest, middle }, ids);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task LimitOutOfRange_Gives400(int limit)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await GetRidesAsync(customer.Token, $"?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Driver_SeesOnlyRidesAssignedToThem()
    {
        var c1 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var c2 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var assigned = await InsertRideAsync(c1.Id, RideStatus.Accepted, driver.Id);
        var unassigned = await InsertRideAsync(c2.Id, RideStatus.Requested);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Equal(new[] { assigned }, ids);
        Assert.DoesNotContain(unassigned, ids);
    }

    [Fact]
    public async Task Admin_SeesEveryonesRides()
    {
        var c1 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var c2 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var ride1 = await InsertRideAsync(c1.Id, RideStatus.Requested);
        var ride2 = await InsertRideAsync(c2.Id, RideStatus.Requested);

        // limit=50: the shared test DB holds rides from other tests, ours are the newest.
        var ids = await GetIdsAsync(admin.Token, "?limit=50");

        Assert.Contains(ride1, ids);
        Assert.Contains(ride2, ids);
    }

    [Fact]
    public async Task Response_ContainsTheRideDetails()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await GetRidesAsync(customer.Token);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(rideId, item.GetProperty("id").GetInt32());
        Assert.Equal("Requested", item.GetProperty("status").GetString());
        Assert.Equal(14.20m, item.GetProperty("estimatedFare").GetDecimal());
        Assert.Equal(1, item.GetProperty("vehicleTypeId").GetInt32());
    }
}