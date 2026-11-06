using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class CancelRideTests : IClassFixture<TaxiApiFactory>
{
    private const double PickupLat = 47.0267, PickupLon = -65.4631;
    private const double DropoffLat = 47.0400, DropoffLon = -65.5000;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public CancelRideTests(TaxiApiFactory factory)
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
            Name = "Cancel-" + Guid.NewGuid().ToString("N")[..12],
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
        int vehicleTypeId, RideStatus status, int? driverId = null)
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

    private Task<HttpResponseMessage> CancelAsync(string? token, int rideId, object? body = null) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/cancel", token, body);

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(null, rideId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Driver)]
    [InlineData(Role.Admin)]
    public async Task NonCustomer_Gives403(Role role)
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Requested);
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await CancelAsync(user.Token, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- cancelling ----------

    [Fact]
    public async Task Customer_CancelsRequestedRide_AndItIsSaved()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(customer.Token, rideId, new { reason = "Changed my mind" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Customer", doc.RootElement.GetProperty("cancelledBy").GetString());
        Assert.Equal("Changed my mind", doc.RootElement.GetProperty("cancelReason").GetString());

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Cancelled, ride.Status);
        Assert.NotNull(ride.CancelledAt);
        Assert.Equal((Role?)Role.Customer, ride.CancelledBy);
        Assert.Equal("Changed my mind", ride.CancelReason);
    }

    [Fact]
    public async Task Customer_CancelsAcceptedRide_AndDriverIsFreed()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);
        var (nextRide, _) = await InsertRideAsync(type, RideStatus.Requested);

        var cancel = await CancelAsync(customer.Token, rideId);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(RideStatus.Cancelled, (await ReadRideAsync(rideId)).Status);

        var accept = await TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{nextRide}/accept", driver.Token);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    [Fact]
    public async Task CancellingWithoutBody_Works_AndReasonIsNull()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(customer.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadRideAsync(rideId)).CancelReason);
    }

    [Fact]
    public async Task WhitespaceReason_IsStoredAsNull()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(customer.Token, rideId, new { reason = "   " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadRideAsync(rideId)).CancelReason);
    }

    [Fact]
    public async Task ReasonOf200Characters_IsAccepted()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(customer.Token, rideId, new { reason = new string('x', 200) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReasonOf201Characters_Gives400_AndRideIsUntouched()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var response = await CancelAsync(customer.Token, rideId, new { reason = new string('x', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- ownership / state ----------

    [Fact]
    public async Task OtherCustomer_Gets404_AndRideIsUntouched()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, _) = await InsertRideAsync(type, RideStatus.Requested);
        var stranger = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await CancelAsync(stranger.Token, rideId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task MissingRide_Gets404()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await CancelAsync(customer.Token, 999999999);

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
        var (rideId, customer) = await InsertRideAsync(type, status, driver.Id);

        var response = await CancelAsync(customer.Token, rideId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(status, ride.Status);
        Assert.Null(ride.CancelledAt);
    }

    [Fact]
    public async Task CancellingTwice_SecondTimeGives409()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var first = await CancelAsync(customer.Token, rideId);
        var second = await CancelAsync(customer.Token, rideId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CancellingMyRide_DoesNotTouchOtherPeoplesRides()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var (mine, me) = await InsertRideAsync(type, RideStatus.Requested);
        var (othersRequested, _) = await InsertRideAsync(type, RideStatus.Requested);
        var (othersAccepted, _) = await InsertRideAsync(type, RideStatus.Accepted, driver.Id);

        var response = await CancelAsync(me.Token, mine);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RideStatus.Cancelled, (await ReadRideAsync(mine)).Status);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(othersRequested)).Status);
        var accepted = await ReadRideAsync(othersAccepted);
        Assert.Equal(RideStatus.Accepted, accepted.Status);
        Assert.Equal(driver.Id, accepted.DriverProfileUserId);
    }

    // ---------- races and what happens afterwards ----------

    [Fact]
    public async Task CancelAndAcceptAtTheSameTime_RideEndsCancelled()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        var cancelTask = CancelAsync(customer.Token, rideId);
        var acceptTask = TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/accept", driver.Token);
        await Task.WhenAll(cancelTask, acceptTask);

        // The cancel always works: the ride is Requested or Accepted, both cancellable.
        Assert.Equal(HttpStatusCode.OK, (await cancelTask).StatusCode);
        // The accept either got in first (200) or found it already cancelled (409).
        Assert.Contains((await acceptTask).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Equal(RideStatus.Cancelled, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task AfterCancelling_CustomerSeesCancelled_AndCanRequestANewRide()
    {
        var type = await CreateVehicleTypeAsync();
        var (rideId, customer) = await InsertRideAsync(type, RideStatus.Requested);

        await CancelAsync(customer.Token, rideId);

        var view = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", customer.Token);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using (var doc = JsonDocument.Parse(await view.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Cancelled", doc.RootElement.GetProperty("status").GetString());
        }

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
}