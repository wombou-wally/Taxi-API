using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class GetRideTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public GetRideTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<int> InsertRideAsync(int customerId, RideStatus status, int? driverId = null)
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
            EstimatedFare = 14.20m
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return ride.Id;
    }

    private async Task UpdateRideAsync(int rideId, RideStatus status, int? driverId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var ride = await context.Rides.FindAsync(rideId);
        Assert.NotNull(ride);
        ride!.Status = status;
        ride.DriverProfileUserId = driverId;
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> GetRideAsync(string? token, string idText) =>
        TestHelpers.SendAsync(_client, HttpMethod.Get, "/api/rides/" + idText, token);

    private Task<HttpResponseMessage> GetRideAsync(string? token, int id) =>
        GetRideAsync(token, id.ToString());

    [Fact]
    public async Task NoToken_Gives401()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await GetRideAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CanReadOwnRide()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await GetRideAsync(customer.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(rideId, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Requested", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(14.20m, doc.RootElement.GetProperty("estimatedFare").GetDecimal());
    }

    [Fact]
    public async Task Customer_CannotReadSomeoneElsesRide_Gets404()
    {
        var owner = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var stranger = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(owner.Id, RideStatus.Requested);

        var response = await GetRideAsync(stranger.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingRide_Gets404()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await GetRideAsync(customer.Token, 999999999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingRide_And_ForbiddenRide_LookTheSame()
    {
        var owner = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var stranger = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(owner.Id, RideStatus.Requested);

        var forbidden = await GetRideAsync(stranger.Token, rideId);
        var missing = await GetRideAsync(stranger.Token, 999999999);

        Assert.Equal(missing.StatusCode, forbidden.StatusCode);
    }

    [Fact]
    public async Task AssignedDriver_CanReadTheRide()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Accepted, driver.Id);

        var response = await GetRideAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OtherDriver_Gets404()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var assigned = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var other = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Accepted, assigned.Id);

        var response = await GetRideAsync(other.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedRide_IsInvisibleToDrivers()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await GetRideAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadAnyRide()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await GetRideAsync(admin.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonNumericId_Gets404()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await GetRideAsync(customer.Token, "abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Polling_SeesTheStatusChange()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var before = await GetRideAsync(customer.Token, rideId);
        using (var doc = JsonDocument.Parse(await before.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Requested", doc.RootElement.GetProperty("status").GetString());
        }

        await UpdateRideAsync(rideId, RideStatus.Accepted, driver.Id);

        var after = await GetRideAsync(customer.Token, rideId);
        using (var doc = JsonDocument.Parse(await after.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Accepted", doc.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task RequestRide_LocationHeader_PointsAtTheNewRide()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var body = new
        {
            pickupLat = 47.0267,
            pickupLog = -65.4631,
            dropoffLat = 47.0400,
            dropoffLog = -65.5000,
            vehicleTypeId = 1
        };
        var created = await TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/rides", customer.Token, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var location = created.Headers.Location;
        Assert.NotNull(location);

        var followed = await TestHelpers.SendAsync(_client, HttpMethod.Get, location!.PathAndQuery, customer.Token);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }
}