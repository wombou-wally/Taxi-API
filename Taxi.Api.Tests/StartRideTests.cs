using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class StartRideTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public StartRideTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    // Each ride gets its own customer (one active ride per customer is enforced by the DB).
    private async Task<(int RideId, TestUser Customer)> InsertRideAsync(RideStatus status, int? driverId)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var ride = new Ride
        {
            CustomerId = customer.Id,
            DriverProfileUserId = driverId,
            VehicleTypeId = 1,
            Status = status,
            PickupLatitude = 47.0267,
            PickupLongitude = -65.4631,
            DropoffLatitude = 47.0400,
            DropoffLongitude = -65.5000,
            EstimatedFare = 14.20m,
            AcceptedAt = driverId.HasValue ? DateTime.UtcNow : null
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return (ride.Id, customer);
    }

    private async Task<Ride> ReadRideAsync(int rideId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.Rides.AsNoTracking().FirstAsync(r => r.Id == rideId);
    }

    private Task<HttpResponseMessage> StartAsync(string? token, int rideId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/start", token);

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, driver.Id);

        var response = await StartAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Admin)]
    public async Task NonDriver_Gives403(Role role)
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, driver.Id);
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await StartAsync(user.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Accepted, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- starting ----------

    [Fact]
    public async Task AssignedDriver_StartsAcceptedRide_AndItIsSaved()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, driver.Id);

        var response = await StartAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(rideId, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("InProgress", doc.RootElement.GetProperty("status").GetString());

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.InProgress, ride.Status);
        Assert.NotNull(ride.StartedAt);
        Assert.Equal(driver.Id, ride.DriverProfileUserId);
    }

    [Fact]
    public async Task OtherDriver_Gets404_AndRideIsUntouched()
    {
        var assigned = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var other = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, assigned.Id);

        var response = await StartAsync(other.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Accepted, ride.Status);
        Assert.Null(ride.StartedAt);
    }

    [Fact]
    public async Task MissingRide_Gets404()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        var response = await StartAsync(driver.Token, 999999999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedRequestedRide_Gets404()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Requested, null);

        var response = await StartAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Theory]
    [InlineData(RideStatus.InProgress)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task RideInWrongState_Gives409_AndIsUnchanged(RideStatus status)
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(status, driver.Id);

        var response = await StartAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(status, ride.Status);
        Assert.Null(ride.StartedAt);
    }

    [Fact]
    public async Task StartingTwice_SecondTimeGives409()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, driver.Id);

        var first = await StartAsync(driver.Token, rideId);
        var second = await StartAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task StartingTwiceAtOnce_OnlyOneWins()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(RideStatus.Accepted, driver.Id);

        var responses = await Task.WhenAll(
            StartAsync(driver.Token, rideId),
            StartAsync(driver.Token, rideId));

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => (int)c).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);
        Assert.Equal(RideStatus.InProgress, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- what the customer sees ----------

    [Fact]
    public async Task AfterStarting_CustomerSeesInProgress()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, customer) = await InsertRideAsync(RideStatus.Accepted, driver.Id);

        var start = await StartAsync(driver.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var view = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", customer.Token);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using var doc = JsonDocument.Parse(await view.Content.ReadAsStringAsync());
        Assert.Equal("InProgress", doc.RootElement.GetProperty("status").GetString());
    }
}