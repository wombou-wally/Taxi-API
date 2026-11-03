using System.Net;
using System.Net.Http.Json;
using Taxi.Api.DTO.Driver;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class DriverAvailabilityTests : IClassFixture<TaxiApiFactory>
{
    private const string OnlineUrl = "api/drivers/online";
    private const string OfflineUrl = "api/drivers/offline";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public DriverAvailabilityTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<TestUser> NewDriverAsync() => TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

    private async Task<TestUser> NewApprovedDriverAsync()
    {
        var driver = await NewDriverAsync();
        await TestHelpers.ApproveDriverAsync(_factory, driver.Id);
        return driver;
    }

    private Task<HttpResponseMessage> GoOnlineAsync(TestUser driver, int vehicleId) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, OnlineUrl, driver.Token, new { VehicleId = vehicleId });

    [Fact]
    public async Task Online_NoToken_Returns401()
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, OnlineUrl, null, new { VehicleId = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Online_CustomerToken_Returns403()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, OnlineUrl, customer.Token, new { VehicleId = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Online_PendingDriver_Returns403_AndStaysOffline()
    {
        var driver = await NewDriverAsync();
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        var response = await GoOnlineAsync(driver, vehicleId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.False(profile.IsOnline);
        Assert.Null(profile.ActiveVehicleId);
    }

    [Fact]
    public async Task Online_ApprovedDriver_OwnVehicle_Returns200_AndSavesState()
    {
        var driver = await NewApprovedDriverAsync();
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        var response = await GoOnlineAsync(driver, vehicleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DriverAvailabilityResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsOnline);
        Assert.Equal(vehicleId, body.VehicleId);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.True(profile.IsOnline);
        Assert.Equal(vehicleId, profile.ActiveVehicleId);
    }

    [Fact]
    public async Task Online_OtherDriversVehicle_Returns404_AndStaysOffline()
    {
        var owner = await NewApprovedDriverAsync();
        var intruder = await NewApprovedDriverAsync();
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, owner.Id);

        var response = await GoOnlineAsync(intruder, vehicleId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await TestHelpers.GetProfileAsync(_factory, intruder.Id)).IsOnline);
    }

    [Fact]
    public async Task Online_UnknownVehicle_Returns404()
    {
        var driver = await NewApprovedDriverAsync();

        var response = await GoOnlineAsync(driver, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Online_VehicleIdZero_Returns400()
    {
        var driver = await NewApprovedDriverAsync();

        var response = await GoOnlineAsync(driver, 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Online_ExpiredInsurance_Returns409_AndStaysOffline()
    {
        var driver = await NewApprovedDriverAsync();
        var expired = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id, expired);

        var response = await GoOnlineAsync(driver, vehicleId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False((await TestHelpers.GetProfileAsync(_factory, driver.Id)).IsOnline);
    }

    [Fact]
    public async Task Online_SwitchingVehicle_ChangesActiveVehicle()
    {
        var driver = await NewApprovedDriverAsync();
        var first = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);
        var second = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        Assert.Equal(HttpStatusCode.OK, (await GoOnlineAsync(driver, first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GoOnlineAsync(driver, second)).StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.True(profile.IsOnline);
        Assert.Equal(second, profile.ActiveVehicleId);
    }

    [Fact]
    public async Task Offline_CustomerToken_Returns403()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, OfflineUrl, customer.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Offline_ClearsOnlineStateAndVehicle()
    {
        var driver = await NewApprovedDriverAsync();
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);
        Assert.Equal(HttpStatusCode.OK, (await GoOnlineAsync(driver, vehicleId)).StatusCode);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, OfflineUrl, driver.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.False(profile.IsOnline);
        Assert.Null(profile.ActiveVehicleId);
    }

    [Fact]
    public async Task Offline_WhenAlreadyOffline_Returns200()
    {
        var driver = await NewDriverAsync();

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, OfflineUrl, driver.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteActiveVehicle_Returns409_UntilDriverGoesOffline()
    {
        var driver = await NewApprovedDriverAsync();
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);
        Assert.Equal(HttpStatusCode.OK, (await GoOnlineAsync(driver, vehicleId)).StatusCode);

        var blocked = await TestHelpers.SendAsync(_client, HttpMethod.Delete, $"api/driver/vehicle/{vehicleId}", driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await TestHelpers.SendAsync(_client, HttpMethod.Post, OfflineUrl, driver.Token);

        var allowed = await TestHelpers.SendAsync(_client, HttpMethod.Delete, $"api/driver/vehicle/{vehicleId}", driver.Token);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }
}