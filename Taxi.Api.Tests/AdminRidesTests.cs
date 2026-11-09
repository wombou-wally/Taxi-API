using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class AdminRidesTests : IClassFixture<TaxiApiFactory>
{
    private const string ListUrl = "/api/admin/rides";
    private const double PickupLat = 47.0267, PickupLon = -65.4631;
    private const double DropoffLat = 47.0400, DropoffLon = -65.5000;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public AdminRidesTests(TaxiApiFactory factory)
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
            Name = "AdminRides-" + Guid.NewGuid().ToString("N")[..12],
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

    // Remember: a customer may only have ONE active ride, so several rides
    // for the same customer must be Completed or Cancelled.
    private async Task<int> InsertRideAsync(
        int customerId,
        RideStatus status,
        int? driverId = null,
        DateTime? requestedAt = null,
        int vehicleTypeId = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var ride = new Ride
        {
            CustomerId = customerId,
            DriverProfileUserId = driverId,
            VehicleTypeId = vehicleTypeId,
            Status = status,
            PickupLatitude = PickupLat,
            PickupLongitude = PickupLon,
            DropoffLatitude = DropoffLat,
            DropoffLongitude = DropoffLon,
            EstimatedFare = 14.20m,
            RequestedAt = requestedAt ?? DateTime.UtcNow,
            AcceptedAt = driverId.HasValue ? DateTime.UtcNow.AddMinutes(-10) : null,
            StartedAt = status == RideStatus.InProgress ? DateTime.UtcNow.AddMinutes(-5) : null
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return ride.Id;
    }

    private async Task<Ride> ReadRideAsync(int rideId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.Rides.AsNoTracking().FirstAsync(r => r.Id == rideId);
    }

    private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("o"));

    private async Task<(List<JsonElement> Items, int Total, int Page, int PageSize)> ListAsync(string token, string query)
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get, ListUrl + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        return (items,
            doc.RootElement.GetProperty("totalCount").GetInt32(),
            doc.RootElement.GetProperty("page").GetInt32(),
            doc.RootElement.GetProperty("pageSize").GetInt32());
    }

    private static List<int> IdsOf(IEnumerable<JsonElement> items) =>
        items.Select(i => i.GetProperty("id").GetInt32()).ToList();

    private Task<HttpResponseMessage> ForceCancelAsync(string? token, int rideId, object? body) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"{ListUrl}/{rideId}/cancel", token, body);

    // ---------- who may call it ----------

    [Fact]
    public async Task List_NoToken_Gives401()
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get, ListUrl, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Driver)]
    public async Task List_NonAdmin_Gives403(Role role)
    {
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get, ListUrl, user.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForceCancel_NoToken_Gives401()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await ForceCancelAsync(null, rideId, new { reason = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Driver)]
    public async Task ForceCancel_NonAdmin_Gives403_AndRideIsUntouched(Role role)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await ForceCancelAsync(user.Token, rideId, new { reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    // ---------- list: filters ----------

    [Fact]
    public async Task Admin_SeesRidesOfDifferentCustomers()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var a = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var b = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideA = await InsertRideAsync(a.Id, RideStatus.Requested);
        var rideB = await InsertRideAsync(b.Id, RideStatus.Requested);

        var (items, _, _, _) = await ListAsync(admin.Token, "?pageSize=100");

        var ids = IdsOf(items);
        Assert.Contains(rideA, ids);
        Assert.Contains(rideB, ids);
    }

    [Fact]
    public async Task Filter_ByCustomer_ReturnsOnlyThatCustomersRides()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var a = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var b = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideA = await InsertRideAsync(a.Id, RideStatus.Completed);
        await InsertRideAsync(b.Id, RideStatus.Completed);

        var (items, total, _, _) = await ListAsync(admin.Token, $"?customerId={a.Id}");

        Assert.Equal(new[] { rideA }, IdsOf(items));
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Filter_ByDriver_ReturnsOnlyThatDriversRides()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var otherDriver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var c1 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var c2 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var c3 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var r1 = await InsertRideAsync(c1.Id, RideStatus.Completed, driver.Id);
        var r2 = await InsertRideAsync(c2.Id, RideStatus.Completed, driver.Id);
        await InsertRideAsync(c3.Id, RideStatus.Completed, otherDriver.Id);

        var (items, total, _, _) = await ListAsync(admin.Token, $"?driverId={driver.Id}");

        Assert.Equal(2, total);
        Assert.Equal(new[] { r1, r2 }.OrderBy(x => x), IdsOf(items).OrderBy(x => x));
    }

    [Fact]
    public async Task Filter_ByStatus_AndCustomer_Combined()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(customer.Id, RideStatus.Completed);
        var cancelled1 = await InsertRideAsync(customer.Id, RideStatus.Cancelled);
        var cancelled2 = await InsertRideAsync(customer.Id, RideStatus.Cancelled);

        var (items, total, _, _) = await ListAsync(admin.Token, $"?customerId={customer.Id}&status=Cancelled");

        Assert.Equal(2, total);
        Assert.Equal(new[] { cancelled1, cancelled2 }.OrderBy(x => x), IdsOf(items).OrderBy(x => x));
        Assert.All(items, i => Assert.Equal("Cancelled", i.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Filter_ByDates_FromAndTo()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var now = DateTime.UtcNow;
        var oldest = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: now.AddDays(-3));
        var middle = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: now.AddDays(-2));
        var newest = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: now.AddDays(-1));
        var cid = customer.Id;

        var (fromOnly, _, _, _) = await ListAsync(admin.Token, $"?customerId={cid}&from={Iso(now.AddDays(-2.5))}");
        var (toOnly, _, _, _) = await ListAsync(admin.Token, $"?customerId={cid}&to={Iso(now.AddDays(-1.5))}");
        var (both, _, _, _) = await ListAsync(admin.Token,
            $"?customerId={cid}&from={Iso(now.AddDays(-2.5))}&to={Iso(now.AddDays(-1.5))}");

        Assert.Equal(new[] { newest, middle }, IdsOf(fromOnly));
        Assert.Equal(new[] { middle, oldest }, IdsOf(toOnly));
        Assert.Equal(new[] { middle }, IdsOf(both));
    }

    // ---------- list: pagination and order ----------

    [Fact]
    public async Task Pagination_SplitsIntoPages_WithCorrectTotal_AndNoOverlap()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var now = DateTime.UtcNow;
        var created = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            created.Add(await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: now.AddHours(-i)));
        }
        var cid = customer.Id;

        var (page1, total1, p1, size1) = await ListAsync(admin.Token, $"?customerId={cid}&pageSize=2&page=1");
        var (page2, _, _, _) = await ListAsync(admin.Token, $"?customerId={cid}&pageSize=2&page=2");
        var (page3, _, _, _) = await ListAsync(admin.Token, $"?customerId={cid}&pageSize=2&page=3");
        var (page4, _, _, _) = await ListAsync(admin.Token, $"?customerId={cid}&pageSize=2&page=4");

        Assert.Equal(5, total1);
        Assert.Equal(1, p1);
        Assert.Equal(2, size1);
        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Single(page3);
        Assert.Empty(page4);

        // Newest first, so the pages together are the created rides in creation order (index 0 is newest).
        var all = IdsOf(page1).Concat(IdsOf(page2)).Concat(IdsOf(page3)).ToList();
        Assert.Equal(created, all);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public async Task SameTimestamp_IsOrderedByIdDescending()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var sameTime = DateTime.UtcNow.AddHours(-1);
        var first = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: sameTime);
        var second = await InsertRideAsync(customer.Id, RideStatus.Completed, requestedAt: sameTime);

        var (items, _, _, _) = await ListAsync(admin.Token, $"?customerId={customer.Id}");

        Assert.Equal(new[] { second, first }, IdsOf(items));
    }

    [Fact]
    public async Task Items_ShowNullForMissingDriverAndTimestamps()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(customer.Id, RideStatus.Requested);

        var (items, _, _, _) = await ListAsync(admin.Token, $"?customerId={customer.Id}");

        var item = Assert.Single(items);
        Assert.Equal(customer.Id, item.GetProperty("customerId").GetInt32());
        Assert.Equal("Requested", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("driverId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("acceptedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("completedAt").ValueKind);
        Assert.False(string.IsNullOrEmpty(item.GetProperty("vehicleTypeName").GetString()));
    }

    // ---------- list: validation ----------

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=100001")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?status=Banana")]
    public async Task List_InvalidParameters_Give400(string query)
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get, ListUrl + query, admin.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_FromAfterTo_Gives400()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var now = DateTime.UtcNow;

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get,
            $"{ListUrl}?from={Iso(now)}&to={Iso(now.AddDays(-1))}", admin.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- force-cancel ----------

    [Theory]
    [InlineData(RideStatus.Requested)]
    [InlineData(RideStatus.Accepted)]
    [InlineData(RideStatus.InProgress)]
    public async Task ForceCancel_WorksFromEveryActiveState(RideStatus status)
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        int? driverId = status == RideStatus.Requested ? null : driver.Id;
        var rideId = await InsertRideAsync(customer.Id, status, driverId);

        var response = await ForceCancelAsync(admin.Token, rideId, new { reason = "  Safety issue  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Cancelled", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("Admin", doc.RootElement.GetProperty("cancelledBy").GetString());
        Assert.Equal("Safety issue", doc.RootElement.GetProperty("cancelReason").GetString());

        var ride = await ReadRideAsync(rideId);
        Assert.Equal(RideStatus.Cancelled, ride.Status);
        Assert.NotNull(ride.CancelledAt);
        Assert.Equal((Role?)Role.Admin, ride.CancelledBy);
        Assert.Equal("Safety issue", ride.CancelReason);
        Assert.Null(ride.FinalFare);
    }

    [Theory]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task ForceCancel_OnFinishedRide_Gives409_AndIsUnchanged(RideStatus status)
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, status);

        var response = await ForceCancelAsync(admin.Token, rideId, new { reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var ride = await ReadRideAsync(rideId);
        Assert.Equal(status, ride.Status);
        Assert.Null(ride.CancelReason);
    }

    [Fact]
    public async Task ForceCancel_MissingRide_Gives404()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);

        var response = await ForceCancelAsync(admin.Token, 999999999, new { reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForceCancel_ReasonOf200Characters_IsAccepted()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await ForceCancelAsync(admin.Token, rideId, new { reason = new string('x', 200) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ForceCancel_EmptyReason_Gives400_AndRideIsUntouched(string reason)
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await ForceCancelAsync(admin.Token, rideId, new { reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task ForceCancel_MissingReason_Gives400()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await ForceCancelAsync(admin.Token, rideId, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task ForceCancel_ReasonOf201Characters_Gives400()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var rideId = await InsertRideAsync(customer.Id, RideStatus.Requested);

        var response = await ForceCancelAsync(admin.Token, rideId, new { reason = new string('x', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RideStatus.Requested, (await ReadRideAsync(rideId)).Status);
    }

    [Fact]
    public async Task ForceCancel_OfInProgressRide_FreesBothDriverAndCustomer()
    {
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);
        var type = await CreateVehicleTypeAsync();
        var driver = await ReadyDriverAsync(type);
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var otherCustomer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var activeRide = await InsertRideAsync(customer.Id, RideStatus.InProgress, driver.Id, vehicleTypeId: type);
        var waitingRide = await InsertRideAsync(otherCustomer.Id, RideStatus.Requested, vehicleTypeId: type);

        var cancel = await ForceCancelAsync(admin.Token, activeRide, new { reason = "Emergency" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // The customer is free to request again.
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

        // The driver is free to accept another ride.
        var accept = await TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{waitingRide}/accept", driver.Token);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }
}