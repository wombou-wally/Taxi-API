using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class AvailableRidesTests : IClassFixture<TaxiApiFactory>
{
    private const string Url = "/api/rides/available";
    private const double DriverLat = 47.0267, DriverLon = -65.4631;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public AvailableRidesTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // A fresh vehicle type per test: only rides of this type can reach the driver,
    // so old rides in the shared DB can never interfere.
    private async Task<int> CreateVehicleTypeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var type = new VehicleType
        {
            Name = "Avail-" + Guid.NewGuid().ToString("N")[..12],
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

    // An approved, online driver with a location and an active vehicle of the given type.
    // The tweak runs last, so a test can break exactly one thing.
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
    private async Task<int> InsertRideAsync(
        int vehicleTypeId,
        double pickupLat = DriverLat, double pickupLon = DriverLon,
        RideStatus status = RideStatus.Requested,
        int? driverId = null,
        DateTime? requestedAt = null)
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
            PickupLatitude = pickupLat,
            PickupLongitude = pickupLon,
            DropoffLatitude = pickupLat + 0.01,
            DropoffLongitude = pickupLon + 0.01,
            EstimatedFare = 12m,
            RequestedAt = requestedAt ?? DateTime.UtcNow
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return ride.Id;
    }

    private Task<HttpResponseMessage> GetAsync(string? token, string query = "") =>
        TestHelpers.SendAsync(_client, HttpMethod.Get, Url + query, token);

    private async Task<List<int>> GetIdsAsync(string token, string query = "")
    {
        var response = await GetAsync(token, query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
    }

    // ---------- who may call it ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var response = await GetAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Admin)]
    public async Task NonDriver_Gives403(Role role)
    {
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await GetAsync(user.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- driver readiness ----------

    [Fact]
    public async Task DriverNotApproved_Gives403()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.Status = ApprovalStatus.Pending);

        var response = await GetAsync(driver.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DriverOffline_Gives409()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.IsOnline = false);

        var response = await GetAsync(driver.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

        var response = await GetAsync(driver.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DriverWithoutActiveVehicle_Gives409()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type, p => p.ActiveVehicleId = null);

        var response = await GetAsync(driver.Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- which rides are shown ----------

    [Fact]
    public async Task ReadyDriver_SeesNearbyRequestedRide()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var rideId = await InsertRideAsync(type);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Equal(new[] { rideId }, ids);
    }

    [Fact]
    public async Task RideOutsideTheRadius_IsNotShown()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        // Moncton is roughly 115 km away.
        await InsertRideAsync(type, pickupLat: 46.0878, pickupLon: -64.7782);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task RideOfAnotherVehicleType_IsNotShown()
    {
        var myType = await CreateVehicleTypeAsync();
        var otherType = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(myType);
        await InsertRideAsync(otherType);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Empty(ids);
    }

    [Theory]
    [InlineData(RideStatus.Accepted)]
    [InlineData(RideStatus.InProgress)]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task RideNotInRequestedState_IsNotShown(RideStatus status)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var other = await ReadyDriverAsync(type);
        await InsertRideAsync(type, status: status, driverId: other.Id);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task RideAlreadyAssignedToADriver_IsNotShown()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var other = await ReadyDriverAsync(type);
        await InsertRideAsync(type, status: RideStatus.Requested, driverId: other.Id);

        var ids = await GetIdsAsync(driver.Token);

        Assert.Empty(ids);
    }

    // ---------- ordering, limit, privacy ----------

    [Fact]
    public async Task Rides_AreSortedNearestFirst()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        // The near ride is OLDER, so sorting by time would put the far one first.
        var near = await InsertRideAsync(type, pickupLat: 47.0300, requestedAt: DateTime.UtcNow.AddMinutes(-10));
        var mid = await InsertRideAsync(type, pickupLat: 47.0900, requestedAt: DateTime.UtcNow.AddMinutes(-1));

        var response = await GetAsync(driver.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(near, items[0].GetProperty("id").GetInt32());
        Assert.Equal(mid, items[1].GetProperty("id").GetInt32());

        var nearKm = items[0].GetProperty("distanceToPickupKm").GetDouble();
        var midKm = items[1].GetProperty("distanceToPickupKm").GetDouble();
        Assert.True(nearKm < midKm);
        Assert.InRange(midKm, 6.7, 7.4);
    }

    [Fact]
    public async Task Limit_ReturnsAtMostThatMany()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        await InsertRideAsync(type, pickupLat: 47.0270);
        await InsertRideAsync(type, pickupLat: 47.0280);
        await InsertRideAsync(type, pickupLat: 47.0290);

        var ids = await GetIdsAsync(driver.Token, "?limit=2");

        Assert.Equal(2, ids.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task LimitOutOfRange_Gives400(int limit)
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);

        var response = await GetAsync(driver.Token, $"?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Response_DoesNotRevealTheCustomer()
    {
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        await InsertRideAsync(type);

        var response = await GetAsync(driver.Token);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.EnumerateArray());
        foreach (var property in item.EnumerateObject())
        {
            Assert.DoesNotContain("customer", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}