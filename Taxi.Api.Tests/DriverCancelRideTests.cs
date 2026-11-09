using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class DriverCancelRideTests : IClassFixture<TaxiApiFactory>
{
    private const double PickupLat = 47.0267, PickupLon = -65.4631;
    private const double DropoffLat = 47.0400, DropoffLon = -65.5000;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public DriverCancelRideTests(TaxiApiFactory factory)
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
            Name = "DriverCancel-" + Guid.NewGuid().ToString("N")[..12],
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

    private async Task<int?> ActiveVehicleIdAsync(int driverId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.DriverProfiles.AsNoTracking()
            .Where(d => d.UserId == driverId)
            .Select(d => d.ActiveVehicleId)
            .FirstAsync();
    }

    // Each ride gets its own customer (one active ride per customer is enforced by the DB).
    private async Task<(int RideId, TestUser Customer)> InsertRideAsync(
        int vehicleTypeId, RideStatus status, int? driverId = null, int? vehicleId = null)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var ride = new Ride
        {
            CustomerId = customer.Id,
            DriverProfileUserId = driverId,
            VehicleId = vehicleId,
            VehicleTypeId = vehicleTypeId,
            Status = status,
            PickupLatitude = PickupLat,
            PickupLongitude = PickupLon,
            DropoffLatitude = DropoffLat,
            DropoffLongitude = DropoffLon,
            EstimatedFare = 14.20m,
            AcceptedAt = driverId.HasValue ? DateTime.UtcNow.AddMinutes(-10) : null,
            StartedAt = status == RideStatus.InProgress ? DateTime.UtcNow.AddMinutes(-5) : null
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

    private Task<HttpResponseMessage> DropAsync(string? token, int rideId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/driver-cancel", token);

    private Task<HttpResponseMessage> PostAsync(string token, string path) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, path, token);

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        var response = await DropAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Admin)]
    public async Task NonDriver_Gives403(Role role)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await DropAsync(user.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Accepted, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- dropping ----------

    [Fact]
    public async Task AssignedDriver_DropsAcceptedRide_AndItGoesBackToRequested()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var vehicleId = await ActiveVehicleIdAsync(driver.Id);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id, vehicleId);

        var response = await DropAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Null(ride.DriverProfileUserId);
        Assert.Null(ride.VehicleId);
        Assert.Null(ride.AcceptedAt);
        Assert.Null(ride.CancelledAt);
        Assert.Null(ride.CancelReason);
    }

    [Fact]
    public async Task OtherDriver_Gets404_AndRideIsUntouched()
    {
        var type = await CreateVehicleTypeAsync();
        var assigned = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var other = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, assigned.Id);

        var response = await DropAsync(other.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Accepted, ride.Status);
        Assert.Equal(assigned.Id, ride.DriverProfileUserId);
    }

    [Fact]
    public async Task MissingRide_Gets404()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        var response = await DropAsync(driver.Token, 999999999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedRequestedRide_Gets404()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await DropAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(RideStatus.InProgress)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task RideInWrongState_Gives409_AndIsUnchanged(RideStatus status)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, status, driver.Id);

        var response = await DropAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(status, ride.Status);
        Assert.Equal(driver.Id, ride.DriverProfileUserId);
    }

    [Fact]
    public async Task DroppingTwice_SecondTimeGives404()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        var first = await DropAsync(driver.Token, rideId);
        var second = await DropAsync(driver.Token, rideId);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    // ---------- what happens afterwards ----------

    [Fact]
    public async Task AfterDropping_DriverCanAcceptAnotherRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (first, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);
        var (second, _) = await InsertRideAsync(type, RideStatus.Requested);

        var drop = await DropAsync(driver.Token, first);
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);

        var accept = await PostAsync(driver.Token, $"/api/rides/{second}/accept");
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    [Fact]
    public async Task AfterDropping_AnotherDriverSeesTheRide_AndCanAcceptIt()
    {
        var type = await CreateVehicleTypeAsync();
        var dropper = await ReadyDriverAsync(type);
        var other = await ReadyDriverAsync(type);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, dropper.Id);

        await DropAsync(dropper.Token, rideId);

        var list = await TestHelpers.SendAsync(_client, HttpMethod.Get, "/api/rides/available", other.Token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            var ids = doc.RootElement.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
            Assert.Contains(rideId, ids);
        }

        var accept = await PostAsync(other.Token, $"/api/rides/{rideId}/accept");
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(other.Id, (await ReadRideAsync(rideId)).DriverProfileUserId);
    }

    [Fact]
    public async Task AfterDropping_DriverNoLongerSeesTheRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        await DropAsync(driver.Token, rideId);

        var view = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", driver.Token);
        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
    }

    [Fact]
    public async Task AfterDropping_CustomerSeesRequested_WithNoCancelReason_AndCanStillCancel()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        await DropAsync(driver.Token, rideId);

        var view = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", customer.Token);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using (var doc = JsonDocument.Parse(await view.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Requested", doc.RootElement.GetProperty("status").GetString());
            var hasReason = doc.RootElement.TryGetProperty("cancelReason", out var reason)
                            && reason.ValueKind != JsonValueKind.Null;
            Assert.False(hasReason);
        }

        var cancel = await TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/cancel", customer.Token);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
    }

    // ---------- races ----------

    [Fact]
    public async Task DropAndCustomerCancelAtOnce_RideEndsCancelled()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        var dropTask = DropAsync(driver.Token, rideId);
        var cancelTask = TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/cancel", customer.Token);
        await Task.WhenAll(dropTask, cancelTask);

        // The customer's cancel works whether the ride is Accepted or already back to Requested.
        Assert.Equal(HttpStatusCode.OK, (await cancelTask).StatusCode);
        Assert.Contains((await dropTask).StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict });
        Assert.Equal(RideStatus.Cancelled, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task DropAndStartAtOnce_ExactlyOneWins()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        var dropTask = DropAsync(driver.Token, rideId);
        var startTask = PostAsync(driver.Token, $"/api/rides/{rideId}/start");
        await Task.WhenAll(dropTask, startTask);

        var drop = (await dropTask).StatusCode;
        var start = (await startTask).StatusCode;
        var ride = await ReadRideAsync(rideId);

        if (start == HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Conflict, drop);
            Assert.Equal(RideStatus.InProgress, ride.Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.NoContent, drop);
            Assert.Equal(HttpStatusCode.NotFound, start);
            Assert.Equal(RideStatus.Requested, ride.Status);
            Assert.Null(ride.DriverProfileUserId);
        }
    }
}