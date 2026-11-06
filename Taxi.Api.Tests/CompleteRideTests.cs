using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class CompleteRideTests : IClassFixture<TaxiApiFactory>
{
    private const double PickupLat = 47.0267, PickupLon = -65.4631;
    private const double DropoffLat = 47.0400, DropoffLon = -65.5000;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public CompleteRideTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    // Own vehicle type with fixed prices: fares can't be changed by other test classes.
    private async Task<int> CreateVehicleTypeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var type = new VehicleType
        {
            Name = "Complete-" + Guid.NewGuid().ToString("N")[..12],
            Seats = 4,
            BaseFare = 4m,
            PricePerKm = 1.8m,
            PricePerMinute = 0.35m,
            MinimumFare = 7m,
            IsActive = true
        };
        context.VehicleTypes.Add(type);
        await context.SaveChangesAsync();
        return type.Id;
    }

    private async Task<TestUser> ReadyDriverAsync(int vehicleTypeId)
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var vehicle = new Vehicle
        {
            Model = "Test Car",
            PlateNumber = "T" + Guid.NewGuid().ToString("N")[..8],
            InsuranceNumber = "INS-" + Guid.NewGuid().ToString("N")[..8],
            InsuranceExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            DriverProfileUserId = driver.Id,
            VehicleTypeId = vehicleTypeId
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var profile = await context.DriverProfiles.FindAsync(driver.Id);
        Assert.NotNull(profile);
        profile!.Status = ApprovalStatus.Approved;
        profile.IsOnline = true;
        profile.CurrentLatitude = PickupLat;
        profile.CurrentLongitude = PickupLon;
        profile.LocationUpdatedAt = DateTime.UtcNow;
        profile.ActiveVehicleId = vehicle.Id;
        await context.SaveChangesAsync();

        return driver;
    }

    // Each ride gets its own customer (one active ride per customer is enforced by the DB).
    private async Task<(int RideId, TestUser Customer)> InsertRideAsync(
        int vehicleTypeId,
        RideStatus status,
        int? driverId,
        TimeSpan? startedAgo = null,
        decimal estimatedFare = 50m)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var ride = new Ride
        {
            CustomerId = customer.Id,
            DriverProfileUserId = driverId,
            VehicleTypeId = vehicleTypeId,
            Status = status,
            PickupLatitude = PickupLat,
            PickupLongitude = PickupLon,
            DropoffLatitude = DropoffLat,
            DropoffLongitude = DropoffLon,
            EstimatedFare = estimatedFare,
            AcceptedAt = driverId.HasValue ? DateTime.UtcNow.AddHours(-12) : null,
            StartedAt = startedAgo.HasValue ? DateTime.UtcNow - startedAgo.Value : null
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

    private Task<HttpResponseMessage> CompleteAsync(string? token, int rideId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/complete", token);

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(5));

        var response = await CompleteAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Admin)]
    public async Task NonDriver_Gives403(Role role)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(5));
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await CompleteAsync(user.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.InProgress, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- completing ----------

    [Fact]
    public async Task AssignedDriver_CompletesRide_AndFareIsSaved()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(10));

        var response = await CompleteAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Completed, ride.Status);
        Assert.NotNull(ride.CompletedAt);
        Assert.True(ride.CompletedAt >= ride.StartedAt);
        Assert.NotNull(ride.FinalFare);
        Assert.True(ride.FinalFare > 0);
        Assert.True(ride.FinalFare <= ride.EstimatedFare * 2);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Completed", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(ride.FinalFare, doc.RootElement.GetProperty("finalFare").GetDecimal());
    }

    [Fact]
    public async Task LongerTrip_CostsMore_BecauseRealMinutesCount()
    {
        var type = await CreateVehicleTypeAsync();
        var driverA = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var driverB = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (longRide, _) = await InsertRideAsync(type, RideStatus.InProgress, driverA.Id,
            TimeSpan.FromMinutes(30), estimatedFare: 500m);
        var (shortRide, _) = await InsertRideAsync(type, RideStatus.InProgress, driverB.Id,
            TimeSpan.FromMinutes(2), estimatedFare: 500m);

        await CompleteAsync(driverA.Token, longRide);
        await CompleteAsync(driverB.Token, shortRide);

        var longFare = (await ReadRideAsync(longRide)).FinalFare;
        var shortFare = (await ReadRideAsync(shortRide)).FinalFare;
        Assert.NotNull(longFare);
        Assert.NotNull(shortFare);
        Assert.True(longFare > shortFare);
    }

    [Fact]
    public async Task ForgottenRide_FareIsCappedAtTwiceTheEstimate()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id,
            TimeSpan.FromHours(10), estimatedFare: 10m);

        var response = await CompleteAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20m, (await ReadRideAsync(rideId)).FinalFare);
    }

    // ---------- who owns it / what state it is in ----------

    [Fact]
    public async Task OtherDriver_Gets404_AndRideIsUntouched()
    {
        var type = await CreateVehicleTypeAsync();
        var assigned = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var other = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, assigned.Id, TimeSpan.FromMinutes(5));

        var response = await CompleteAsync(other.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.InProgress, ride.Status);
        Assert.Null(ride.FinalFare);
    }

    [Fact]
    public async Task MissingRide_Gets404()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        var response = await CompleteAsync(driver.Token, 999999999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedRequestedRide_Gets404()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Requested, null);

        var response = await CompleteAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(RideStatus.Accepted)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task RideInWrongState_Gives409_AndIsUnchanged(RideStatus status)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, status, driver.Id);

        var response = await CompleteAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(status, ride.Status);
        Assert.Null(ride.FinalFare);
    }

    [Fact]
    public async Task CompletingTwiceAtOnce_OnlyOneWins()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(5));

        var responses = await Task.WhenAll(
            CompleteAsync(driver.Token, rideId),
            CompleteAsync(driver.Token, rideId));

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => (int)c).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);
        Assert.Equal(RideStatus.Completed, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- what happens afterwards ----------

    [Fact]
    public async Task AfterCompleting_CustomerSeesCompletedWithFinalFare()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(8));

        var complete = await CompleteAsync(driver.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var view = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", customer.Token);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using var doc = JsonDocument.Parse(await view.Content.ReadAsStringAsync());
        Assert.Equal("Completed", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal((await ReadRideAsync(rideId)).FinalFare, doc.RootElement.GetProperty("finalFare").GetDecimal());
    }

    [Fact]
    public async Task AfterCompleting_CustomerCanRequestANewRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(5));

        await CompleteAsync(driver.Token, rideId);

        var body = new
        {
            pickupLat = PickupLat,
            pickupLog = PickupLon,
            dropoffLat = DropoffLat,
            dropoffLog = DropoffLon,
            vehicleTypeId = type
        };
        var again = await TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/rides", customer.Token, body);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task AfterCompleting_DriverCanAcceptAnotherRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (firstRide, _) = await InsertRideAsync(type, RideStatus.InProgress, driver.Id, TimeSpan.FromMinutes(5));
        var (secondRide, _) = await InsertRideAsync(type, RideStatus.Requested, null);

        var complete = await CompleteAsync(driver.Token, firstRide);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var accept = await TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{secondRide}/accept", driver.Token);

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(driver.Id, (await ReadRideAsync(secondRide)).DriverProfileUserId);
    }
}