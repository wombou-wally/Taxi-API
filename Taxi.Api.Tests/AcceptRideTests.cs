using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class AcceptRideTests : IClassFixture<TaxiApiFactory>
{
    private const double DriverLat = 47.0267, DriverLon = -65.4631;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public AcceptRideTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    private async Task<int> CreateVehicleTypeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var type = new VehicleType
        {
            Name = "Accept-" + Guid.NewGuid().ToString("N")[..12],
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

    // Approved, online driver with a location and an active vehicle of the given type.
    // The tweak runs last, so a test can break exactly one condition.
    private async Task<TestUser> ReadyDriverAsync(int vehicleTypeId, Action<DriverProfile>? tweak = null)
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
        profile.CurrentLatitude = DriverLat;
        profile.CurrentLongitude = DriverLon;
        profile.LocationUpdatedAt = DateTime.UtcNow;
        profile.ActiveVehicleId = vehicle.Id;
        tweak?.Invoke(profile);
        await context.SaveChangesAsync();

        return driver;
    }

    // Each ride gets its own customer (one active ride per customer is enforced by the DB).
    private async Task<(int RideId, TestUser Customer)> InsertRideAsync(
        int vehicleTypeId,
        RideStatus status = RideStatus.Requested,
        int? driverId = null)
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
            PickupLatitude = DriverLat,
            PickupLongitude = DriverLon,
            DropoffLatitude = DriverLat + 0.01,
            DropoffLongitude = DriverLon + 0.01,
            EstimatedFare = 12m
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

    private async Task<int?> ActiveVehicleIdAsync(int driverId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.DriverProfiles.AsNoTracking()
            .Where(d => d.UserId == driverId)
            .Select(d => d.ActiveVehicleId)
            .FirstAsync();
    }

    private Task<HttpResponseMessage> AcceptAsync(string? token, int rideId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/accept", token);

    private Task<HttpResponseMessage> GetRideAsync(string token, int rideId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", token);

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Admin)]
    public async Task NonDriver_Gives403(Role role)
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, _) = await InsertRideAsync(type);
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await AcceptAsync(user.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- driver readiness ----------

    [Fact]
    public async Task DriverNotApproved_Gives403()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.Status = ApprovalStatus.Pending);
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task DriverOffline_Gives409()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.IsOnline = false);
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task DriverWithoutLocation_Gives409()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p =>
        {
            p.CurrentLatitude = null;
            p.CurrentLongitude = null;
        });
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DriverWithoutActiveVehicle_Gives409()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.ActiveVehicleId = null);
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- accepting ----------

    [Fact]
    public async Task ReadyDriver_AcceptsRide_AndItIsSaved()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (rideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(rideId, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Accepted", doc.RootElement.GetProperty("status").GetString());

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Accepted, ride.Status);
        Assert.Equal(driver.Id, ride.DriverProfileUserId);
        Assert.Equal(await ActiveVehicleIdAsync(driver.Id), ride.VehicleId);
        Assert.NotNull(ride.AcceptedAt);
    }

    [Fact]
    public async Task MissingRide_Gives404()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);

        var response = await AcceptAsync(driver.Token, 999999999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RideOfAnotherVehicleType_Gives409_AndIsUntouched()
    {
        var myType = await CreateVehicleTypeAsync();
        var otherType = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(myType);
        var (rideId, _) = await InsertRideAsync(otherType);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Null(ride.DriverProfileUserId);
    }

    [Fact]
    public async Task RideAlreadyTaken_Gives409_AndFirstDriverKeepsIt()
    {
        var type = await CreateVehicleTypeAsync();
        var first = await ReadyDriverAsync(type);
        var second = await ReadyDriverAsync(type);
        var (rideId, _) = await InsertRideAsync(type);

        var firstResponse = await AcceptAsync(first.Token, rideId);
        var secondResponse = await AcceptAsync(second.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(first.Id, (await ReadRideAsync(rideId)).DriverProfileUserId);
    }

    [Theory]
    [InlineData(RideStatus.Cancelled)]
    [InlineData(RideStatus.Completed)]
    public async Task RideInFinishedState_Gives409(RideStatus status)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (rideId, _) = await InsertRideAsync(type, status);

        var response = await AcceptAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task DriverWithAnActiveRide_CannotAcceptAnother()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        await InsertRideAsync(type, RideStatus.Accepted, driver.Id);
        var (secondRideId, _) = await InsertRideAsync(type);

        var response = await AcceptAsync(driver.Token, secondRideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var second = await ReadRideAsync(secondRideId);
        Assert.Equal(RideStatus.Requested, second.Status);
        Assert.Null(second.DriverProfileUserId);
    }

    // ---------- races ----------

    [Fact]
    public async Task TwoDrivers_AcceptingTheSameRide_OnlyOneWins()
    {
        var type = await CreateVehicleTypeAsync();
        var d1 = await ReadyDriverAsync(type);
        var d2 = await ReadyDriverAsync(type);
        var (rideId, _) = await InsertRideAsync(type);

        var responses = await Task.WhenAll(
            AcceptAsync(d1.Token, rideId),
            AcceptAsync(d2.Token, rideId));

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => (int)c).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Accepted, ride.Status);
        Assert.Contains(ride.DriverProfileUserId, new int?[] { d1.Id, d2.Id });
    }

    [Fact]
    public async Task OneDriver_AcceptingTwoRidesAtOnce_OnlyOneWins()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (ride1, _) = await InsertRideAsync(type);
        var (ride2, _) = await InsertRideAsync(type);

        var responses = await Task.WhenAll(
            AcceptAsync(driver.Token, ride1),
            AcceptAsync(driver.Token, ride2));

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => (int)c).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);

        var rides = new[] { await ReadRideAsync(ride1), await ReadRideAsync(ride2) };
        Assert.Equal(1, rides.Count(r => r.DriverProfileUserId == driver.Id));
    }

    // ---------- what the people involved see afterwards ----------

    [Fact]
    public async Task AfterAccepting_CustomerSeesAccepted_AndDriverCanReadTheRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (rideId, customer) = await InsertRideAsync(type);

        // Before accepting, the ride is invisible to the driver.
        var before = await GetRideAsync(driver.Token, rideId);
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        var accept = await AcceptAsync(driver.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var customerView = await GetRideAsync(customer.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, customerView.StatusCode);
        using (var doc = JsonDocument.Parse(await customerView.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Accepted", doc.RootElement.GetProperty("status").GetString());
        }

        var driverView = await GetRideAsync(driver.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, driverView.StatusCode);
    }
}