using System.Net;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class DriverLocationTests : IClassFixture<TaxiApiFactory>
{
    private const string LocationUrl = "api/drivers/location";
    private const string OnlineUrl = "api/drivers/online";
    private const string OfflineUrl = "api/drivers/offline";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public DriverLocationTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Creates an approved driver with a vehicle and puts them online through the API.
    private async Task<TestUser> NewOnlineDriverAsync()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        await TestHelpers.ApproveDriverAsync(_factory, driver.Id);
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        var online = await TestHelpers.SendAsync(_client, HttpMethod.Post, OnlineUrl, driver.Token, new { VehicleId = vehicleId });
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);

        return driver;
    }

    private Task<HttpResponseMessage> PutLocationAsync(TestUser driver, object body) =>
        TestHelpers.SendAsync(_client, HttpMethod.Put, LocationUrl, driver.Token, body);

    [Fact]
    public async Task Location_NoToken_Returns401()
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Put, LocationUrl, null,
            new { Latitude = 47.0, Longitude = -65.0 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Location_CustomerToken_Returns403()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Put, LocationUrl, customer.Token,
            new { Latitude = 47.0, Longitude = -65.0 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Location_LatitudeOutOfRange_Returns400()
    {
        var driver = await NewOnlineDriverAsync();

        var response = await PutLocationAsync(driver, new { Latitude = 91.0, Longitude = -65.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Location_LongitudeOutOfRange_Returns400()
    {
        var driver = await NewOnlineDriverAsync();

        var response = await PutLocationAsync(driver, new { Latitude = 47.0, Longitude = 181.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Location_MissingLongitude_Returns400()
    {
        var driver = await NewOnlineDriverAsync();

        var response = await PutLocationAsync(driver, new { Latitude = 47.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Location_BoundaryValues_AreAccepted()
    {
        var driver = await NewOnlineDriverAsync();

        var response = await PutLocationAsync(driver, new { Latitude = 90.0, Longitude = -180.0 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Location_WhenOffline_Returns409_AndStoresNothing()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

        var response = await PutLocationAsync(driver, new { Latitude = 47.0267, Longitude = -65.4631 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.Null(profile.CurrentLatitude);
        Assert.Null(profile.CurrentLongitude);
        Assert.Null(profile.LocationUpdatedAt);
    }

    [Fact]
    public async Task Location_WhenOnline_Returns204_AndSavesCoordinatesAndTime()
    {
        var driver = await NewOnlineDriverAsync();

        var response = await PutLocationAsync(driver, new { Latitude = 47.0267, Longitude = -65.4631 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.Equal(47.0267, profile.CurrentLatitude!.Value, 4);
        Assert.Equal(-65.4631, profile.CurrentLongitude!.Value, 4);
        Assert.NotNull(profile.LocationUpdatedAt);
        Assert.True((DateTime.UtcNow - profile.LocationUpdatedAt!.Value).TotalSeconds < 60);
    }

    [Fact]
    public async Task Location_SecondUpdate_ReplacesFirst()
    {
        var driver = await NewOnlineDriverAsync();

        await PutLocationAsync(driver, new { Latitude = 47.0267, Longitude = -65.4631 });
        var second = await PutLocationAsync(driver, new { Latitude = 46.5, Longitude = -64.5 });

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.Equal(46.5, profile.CurrentLatitude!.Value, 4);
        Assert.Equal(-64.5, profile.CurrentLongitude!.Value, 4);
    }

    [Fact]
    public async Task Offline_ClearsLocation()
    {
        var driver = await NewOnlineDriverAsync();
        await PutLocationAsync(driver, new { Latitude = 47.0267, Longitude = -65.4631 });

        var offline = await TestHelpers.SendAsync(_client, HttpMethod.Post, OfflineUrl, driver.Token);
        Assert.Equal(HttpStatusCode.OK, offline.StatusCode);

        var profile = await TestHelpers.GetProfileAsync(_factory, driver.Id);
        Assert.Null(profile.CurrentLatitude);
        Assert.Null(profile.CurrentLongitude);
        Assert.Null(profile.LocationUpdatedAt);
    }
}