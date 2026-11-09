using System.Net;
using System.Text.Json;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class RideLifecycleTests : IClassFixture<TaxiApiFactory>
{
    // Seeded vehicle types (there is no create endpoint).
    private const int TypeA = 1;
    private const int TypeB = 2;

    // xUnit builds a new instance of this class for every test, so each test
    // gets its own random pickup point far away from other tests' rides.
    private readonly double _pickupLat = 10 + Random.Shared.NextDouble() * 50;
    private readonly double _pickupLon = -170 + Random.Shared.NextDouble() * 160;
    private double DropoffLat => _pickupLat + 0.0133;
    private double DropoffLon => _pickupLon - 0.0369;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public RideLifecycleTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // The driver profile already exists: CreateUserAsync registers the driver
    // through /api/drivers/register.
    private async Task<TestUser> OnboardDriverAsync(TestUser admin, int vehicleTypeId)
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        // 1. vehicle
        var vehicle = await ReadJsonAsync(await TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/driver/vehicle", driver.Token, new
        {
            model = "Test Car",
            plateNumber = "T" + Guid.NewGuid().ToString("N")[..8],
            insuranceNumber = "INS-" + Guid.NewGuid().ToString("N")[..8],
            insuranceExpiryDate = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"),
            vehicleTypeId
        }), HttpStatusCode.Created);
        var vehicleId = vehicle.GetProperty("id").GetInt32();

        // 2. admin approves
        await ReadJsonAsync(await TestHelpers.SendAsync(_client, HttpMethod.Post,
            $"/api/drivers/approve/{driver.Id}", admin.Token), HttpStatusCode.OK);

        // 3. online (also sets the active vehicle)
        await ReadJsonAsync(await TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/drivers/online", driver.Token,
            new { vehicleId }), HttpStatusCode.OK);

        // 4. location at this test's pickup point (driver must be online)
        await ReadJsonAsync(await TestHelpers.SendAsync(_client, HttpMethod.Put, "/api/drivers/location", driver.Token,
            new { latitude = _pickupLat, longitude = _pickupLon }), HttpStatusCode.NoContent);

        return driver;
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, params HttpStatusCode[] expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected.Contains(response.StatusCode),
            $"Expected {string.Join("/", expected)} but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        if (string.IsNullOrWhiteSpace(body)) return default;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<TestUser> NewCustomerAsync() =>
        await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

    private async Task<TestUser> NewAdminAsync() =>
        await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);

    private Task<HttpResponseMessage> RequestRideRawAsync(TestUser customer, int vehicleTypeId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/rides", customer.Token, new
        {
            pickupLat = _pickupLat,
            pickupLog = _pickupLon,
            dropoffLat = DropoffLat,
            dropoffLog = DropoffLon,
            vehicleTypeId
        });

    private async Task<int> RequestRideAsync(TestUser customer, int vehicleTypeId)
    {
        var json = await ReadJsonAsync(await RequestRideRawAsync(customer, vehicleTypeId), HttpStatusCode.Created);
        Assert.Equal("Requested", json.GetProperty("status").GetString());
        return json.GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> Act(TestUser user, string action, int rideId, object? body = null) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/rides/{rideId}/{action}", user.Token, body);

    private async Task<JsonElement> GetRideAsync(TestUser user, int rideId) =>
        await ReadJsonAsync(await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", user.Token),
            HttpStatusCode.OK);

    private async Task<string> StatusOfAsync(TestUser user, int rideId) =>
        (await GetRideAsync(user, rideId)).GetProperty("status").GetString()!;

    private async Task<List<int>> AvailableIdsAsync(TestUser driver)
    {
        var json = await ReadJsonAsync(
            await TestHelpers.SendAsync(_client, HttpMethod.Get, "/api/rides/available?limit=50", driver.Token),
            HttpStatusCode.OK);
        return json.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
    }

    private async Task AcceptAsync(TestUser driver, int rideId)
    {
        var json = await ReadJsonAsync(await Act(driver, "accept", rideId), HttpStatusCode.OK);
        Assert.Equal("Accepted", json.GetProperty("status").GetString());
    }

    private async Task StartAsync(TestUser driver, int rideId)
    {
        var json = await ReadJsonAsync(await Act(driver, "start", rideId), HttpStatusCode.OK);
        Assert.Equal("InProgress", json.GetProperty("status").GetString());
    }

    private async Task<JsonElement> CompleteAsync(TestUser driver, int rideId)
    {
        var json = await ReadJsonAsync(await Act(driver, "complete", rideId), HttpStatusCode.OK);
        Assert.Equal("Completed", json.GetProperty("status").GetString());
        return json;
    }

    private static void AssertRefused(HttpResponseMessage response, params HttpStatusCode[] allowed) =>
        Assert.True(allowed.Contains(response.StatusCode),
            $"Expected one of {string.Join("/", allowed)} but got {(int)response.StatusCode}");

    // =====================================================================
    // scenarios
    // =====================================================================

    [Fact]
    public async Task HappyPath_RequestAcceptStartComplete()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();

        var id = await RequestRideAsync(customer, TypeA);
        Assert.Contains(id, await AvailableIdsAsync(driver));

        await AcceptAsync(driver, id);
        Assert.Equal("Accepted", await StatusOfAsync(customer, id));
        Assert.DoesNotContain(id, await AvailableIdsAsync(driver));

        await StartAsync(driver, id);
        Assert.Equal("InProgress", await StatusOfAsync(customer, id));

        var completed = await CompleteAsync(driver, id);
        var estimated = completed.GetProperty("estimatedFare").GetDecimal();
        var final = completed.GetProperty("finalFare").GetDecimal();
        Assert.True(final > 0, $"final fare {final} should be positive");
        Assert.True(final <= estimated * 2m, $"final {final} above 2x estimate {estimated}");

        var seenByCustomer = await GetRideAsync(customer, id);
        Assert.Equal("Completed", seenByCustomer.GetProperty("status").GetString());
        Assert.Equal(final, seenByCustomer.GetProperty("finalFare").GetDecimal());

        // both are free again
        var second = await RequestRideAsync(customer, TypeA);
        await AcceptAsync(driver, second);
    }

    [Fact]
    public async Task CustomerCancelsWhileWaiting_RideDisappears_AndCustomerCanRequestAgain()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();
        var id = await RequestRideAsync(customer, TypeA);

        var cancel = await ReadJsonAsync(await Act(customer, "cancel", id, new { reason = "Changed my mind" }),
            HttpStatusCode.OK);

        Assert.Equal("Cancelled", cancel.GetProperty("status").GetString());
        Assert.Equal("Customer", cancel.GetProperty("cancelledBy").GetString());
        Assert.DoesNotContain(id, await AvailableIdsAsync(driver));
        await RequestRideAsync(customer, TypeA);
    }

    [Fact]
    public async Task CustomerCancelsAfterAccept_DriverIsFree_AndRideCannotBeStarted()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();
        var otherCustomer = await NewCustomerAsync();
        var id = await RequestRideAsync(customer, TypeA);
        await AcceptAsync(driver, id);

        await ReadJsonAsync(await Act(customer, "cancel", id), HttpStatusCode.OK);

        AssertRefused(await Act(driver, "start", id), HttpStatusCode.Conflict);
        var next = await RequestRideAsync(otherCustomer, TypeA);
        await AcceptAsync(driver, next);
    }

    [Fact]
    public async Task DriverDrops_AnotherDriverTakesOver_FirstDriverIsLockedOut()
    {
        var admin = await NewAdminAsync();
        var driverA = await OnboardDriverAsync(admin, TypeA);
        var driverB = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();
        var id = await RequestRideAsync(customer, TypeA);
        await AcceptAsync(driverA, id);

        var drop = await Act(driverA, "driver-cancel", id);
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);
        Assert.Equal("Requested", await StatusOfAsync(customer, id));
        Assert.Contains(id, await AvailableIdsAsync(driverB));

        await AcceptAsync(driverB, id);
        AssertRefused(await Act(driverA, "start", id), HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        await StartAsync(driverB, id);
        AssertRefused(await Act(driverA, "complete", id), HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        await CompleteAsync(driverB, id);
    }

    [Fact]
    public async Task AdminForceCancelsMidRide_BothAreFree_AndAdminListShowsIt()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();
        var id = await RequestRideAsync(customer, TypeA);
        await AcceptAsync(driver, id);
        await StartAsync(driver, id);

        var cancel = await ReadJsonAsync(
            await TestHelpers.SendAsync(_client, HttpMethod.Post, $"/api/admin/rides/{id}/cancel", admin.Token,
                new { reason = "Safety issue" }),
            HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancel.GetProperty("status").GetString());

        AssertRefused(await Act(driver, "complete", id), HttpStatusCode.Conflict);

        var list = await ReadJsonAsync(
            await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/admin/rides?customerId={customer.Id}", admin.Token),
            HttpStatusCode.OK);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("Cancelled", item.GetProperty("status").GetString());
        Assert.Equal("Admin", item.GetProperty("cancelledBy").GetString());

        var again = await RequestRideAsync(customer, TypeA);
        await AcceptAsync(driver, again);
    }

    [Fact]
    public async Task TwoRidesInParallel_DoNotMixUp()
    {
        var admin = await NewAdminAsync();
        var driver1 = await OnboardDriverAsync(admin, TypeA);
        var driver2 = await OnboardDriverAsync(admin, TypeA);
        var customer1 = await NewCustomerAsync();
        var customer2 = await NewCustomerAsync();

        var ride1 = await RequestRideAsync(customer1, TypeA);
        var ride2 = await RequestRideAsync(customer2, TypeA);
        await AcceptAsync(driver1, ride1);
        await AcceptAsync(driver2, ride2);

        // nobody can look at the other pair's ride
        AssertRefused(await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{ride2}", customer1.Token),
            HttpStatusCode.NotFound);
        AssertRefused(await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{ride1}", driver2.Token),
            HttpStatusCode.NotFound);
        AssertRefused(await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{ride1}", customer2.Token),
            HttpStatusCode.NotFound);

        await Task.WhenAll(
            Task.Run(async () => { await StartAsync(driver1, ride1); await CompleteAsync(driver1, ride1); }),
            Task.Run(async () => { await StartAsync(driver2, ride2); await CompleteAsync(driver2, ride2); }));

        Assert.Equal("Completed", await StatusOfAsync(customer1, ride1));
        Assert.Equal("Completed", await StatusOfAsync(customer2, ride2));
    }

    [Fact]
    public async Task WrongPeople_AreRefused_AndRideStateIsUnchanged()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var otherDriver = await OnboardDriverAsync(admin, TypeA);
        var wrongTypeDriver = await OnboardDriverAsync(admin, TypeB);
        var customer = await NewCustomerAsync();
        var stranger = await NewCustomerAsync();
        var id = await RequestRideAsync(customer, TypeA);

        // wrong vehicle type cannot accept
        AssertRefused(await Act(wrongTypeDriver, "accept", id), HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        Assert.Equal("Requested", await StatusOfAsync(customer, id));

        await AcceptAsync(driver, id);

        // another driver cannot start, a stranger cannot cancel, the customer cannot complete
        AssertRefused(await Act(otherDriver, "start", id), HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        AssertRefused(await Act(stranger, "cancel", id), HttpStatusCode.NotFound);
        AssertRefused(await Act(customer, "complete", id), HttpStatusCode.Forbidden);
        Assert.Equal("Accepted", await StatusOfAsync(customer, id));
    }

    [Fact]
    public async Task OnlyOneActiveRide_PerCustomer_AndPerDriver()
    {
        var admin = await NewAdminAsync();
        var driver = await OnboardDriverAsync(admin, TypeA);
        var customer = await NewCustomerAsync();
        var otherCustomer = await NewCustomerAsync();
        var first = await RequestRideAsync(customer, TypeA);
        var other = await RequestRideAsync(otherCustomer, TypeA);

        // customer cannot have two active rides
        AssertRefused(await RequestRideRawAsync(customer, TypeA), HttpStatusCode.Conflict);

        // driver cannot hold two active rides
        await AcceptAsync(driver, first);
        AssertRefused(await Act(driver, "accept", other), HttpStatusCode.Conflict);
        Assert.Equal("Requested", await StatusOfAsync(otherCustomer, other));

        // after finishing, the driver can take the next one
        await StartAsync(driver, first);
        await CompleteAsync(driver, first);
        await AcceptAsync(driver, other);
    }
}