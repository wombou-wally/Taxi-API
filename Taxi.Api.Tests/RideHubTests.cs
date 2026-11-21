using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class RideHubTests : IClassFixture<TaxiApiFactory>
{
    private const double Lat = 47.0267, Lon = -65.4631;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public RideHubTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- helpers ----------

    // Builds a SignalR connection that talks to the in-memory test server.
    // Long polling is used because the test server has no real network socket.
    // Every message the hub sends is written to the channel as text, so a test can read it back.
    private HubConnection BuildConnection(string? token, Channel<string> events)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/rides"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();

        connection.On<int, string>("RideChanged", (rideId, status) =>
        {
            events.Writer.TryWrite($"RideChanged:{rideId}:{status}");
        });
        connection.On("AvailableRidesChanged", () =>
        {
            events.Writer.TryWrite("AvailableRidesChanged");
        });

        return connection;
    }

    // Starts the connection, then gives the hub a moment to add it to its groups.
    // StartAsync can return before OnConnectedAsync has finished, so without the pause
    // an event sent immediately afterwards could miss the new connection.
    private static async Task StartAsync(HubConnection connection)
    {
        await connection.StartAsync();
        await Task.Delay(300);
    }

    // Returns the next message, or null if nothing arrives in time.
    private static async Task<string?> NextAsync(Channel<string> events, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await events.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // Approved, online driver with a location and an active vehicle (vehicle type 1 is seeded).
    private async Task<TestUser> ReadyDriverAsync()
    {
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        await TestHelpers.ApproveDriverAsync(_factory, driver.Id);
        int vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var profile = await context.DriverProfiles.SingleAsync(d => d.UserId == driver.Id);
        profile.IsOnline = true;
        profile.CurrentLatitude = Lat;
        profile.CurrentLongitude = Lon;
        profile.LocationUpdatedAt = DateTime.UtcNow;
        profile.ActiveVehicleId = vehicleId;
        await context.SaveChangesAsync();

        return driver;
    }

    // A Requested ride for a brand new customer (one active ride per customer is enforced by the DB).
    private async Task<(int RideId, TestUser Customer)> RequestedRideAsync()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var ride = new Ride
        {
            CustomerId = customer.Id,
            VehicleTypeId = 1,
            Status = RideStatus.Requested,
            PickupLatitude = Lat,
            PickupLongitude = Lon,
            DropoffLatitude = Lat + 0.01,
            DropoffLongitude = Lon + 0.01,
            EstimatedFare = 12m
        };
        context.Rides.Add(ride);
        await context.SaveChangesAsync();
        return (ride.Id, customer);
    }

    private Task<HttpResponseMessage> PostAsync(string url, string token, object? body = null)
        => TestHelpers.SendAsync(_client, HttpMethod.Post, url, token, body);

    // ---------- tests ----------

    [Fact]
    public async Task Connect_WithoutToken_IsRejected()
    {
        var events = Channel.CreateUnbounded<string>();
        await using var connection = BuildConnection(null, events);

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Accept_NotifiesCustomer_AndDriversList()
    {
        var driver = await ReadyDriverAsync();
        var (rideId, customer) = await RequestedRideAsync();

        var customerEvents = Channel.CreateUnbounded<string>();
        var driverEvents = Channel.CreateUnbounded<string>();
        await using var customerConnection = BuildConnection(customer.Token, customerEvents);
        await using var driverConnection = BuildConnection(driver.Token, driverEvents);
        await StartAsync(customerConnection);
        await StartAsync(driverConnection);

        var response = await PostAsync($"api/rides/{rideId}/accept", driver.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal($"RideChanged:{rideId}:Accepted", await NextAsync(customerEvents));

        var first = await NextAsync(driverEvents);
        var second = await NextAsync(driverEvents);
        Assert.Contains($"RideChanged:{rideId}:Accepted", new[] { first, second });
        Assert.Contains("AvailableRidesChanged", new[] { first, second });
    }

    [Fact]
    public async Task Lifecycle_SendsEveryStatusToCustomer()
    {
        var driver = await ReadyDriverAsync();
        var (rideId, customer) = await RequestedRideAsync();

        var customerEvents = Channel.CreateUnbounded<string>();
        await using var customerConnection = BuildConnection(customer.Token, customerEvents);
        await StartAsync(customerConnection);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"api/rides/{rideId}/accept", driver.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"api/rides/{rideId}/start", driver.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"api/rides/{rideId}/complete", driver.Token)).StatusCode);

        Assert.Equal($"RideChanged:{rideId}:Accepted", await NextAsync(customerEvents));
        Assert.Equal($"RideChanged:{rideId}:InProgress", await NextAsync(customerEvents));
        Assert.Equal($"RideChanged:{rideId}:Completed", await NextAsync(customerEvents));
    }

    [Fact]
    public async Task OtherCustomer_DoesNotReceiveMyRideEvents()
    {
        var driver = await ReadyDriverAsync();
        var (rideId, _) = await RequestedRideAsync();
        var stranger = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var strangerEvents = Channel.CreateUnbounded<string>();
        await using var strangerConnection = BuildConnection(stranger.Token, strangerEvents);
        await StartAsync(strangerConnection);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"api/rides/{rideId}/accept", driver.Token)).StatusCode);

        Assert.Null(await NextAsync(strangerEvents, 1500));
    }

    [Fact]
    public async Task CustomerCancel_OfOpenRide_RefreshesDriversList()
    {
        var driver = await ReadyDriverAsync();
        var (rideId, customer) = await RequestedRideAsync();

        var driverEvents = Channel.CreateUnbounded<string>();
        await using var driverConnection = BuildConnection(driver.Token, driverEvents);
        await StartAsync(driverConnection);

        var response = await PostAsync($"api/rides/{rideId}/cancel", customer.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal("AvailableRidesChanged", await NextAsync(driverEvents));
    }

    [Fact]
    public async Task DriverCancel_TellsCustomerRideIsRequestedAgain()
    {
        var driver = await ReadyDriverAsync();
        var (rideId, customer) = await RequestedRideAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"api/rides/{rideId}/accept", driver.Token)).StatusCode);

        var customerEvents = Channel.CreateUnbounded<string>();
        await using var customerConnection = BuildConnection(customer.Token, customerEvents);
        await StartAsync(customerConnection);

        var response = await PostAsync($"api/rides/{rideId}/driver-cancel", driver.Token);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal($"RideChanged:{rideId}:Requested", await NextAsync(customerEvents));
    }
}
