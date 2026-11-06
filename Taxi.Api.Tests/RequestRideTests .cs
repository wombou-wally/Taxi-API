using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class RequestRideTests : IClassFixture<TaxiApiFactory>
{
    private const string RidesUrl = "/api/rides";
    private const double PickupLat = 47.0267, PickupLon = -65.4631;
    private const double DropoffLat = 47.0400, DropoffLon = -65.5000;

    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public RequestRideTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // The only place that knows the JSON names of the request.
    private static object Body(
        int vehicleTypeId = 1,
        double pickupLat = PickupLat, double pickupLog = PickupLon,
        double dropoffLat = DropoffLat, double dropoffLog = DropoffLon) =>
        new { pickupLat, pickupLog, dropoffLat, dropoffLog, vehicleTypeId };

    private Task<HttpResponseMessage> RequestAsync(string? token, object body) =>
        TestHelpers.SendAsync(_client, HttpMethod.Post, RidesUrl, token, body);

    private async Task<List<Ride>> RidesOfAsync(int customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.Rides.AsNoTracking().Where(r => r.CustomerId == customerId).ToListAsync();
    }

    private async Task InsertRideAsync(int customerId, RideStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        context.Rides.Add(new Ride
        {
            CustomerId = customerId,
            VehicleTypeId = 1,
            Status = status,
            PickupLatitude = PickupLat,
            PickupLongitude = PickupLon,
            DropoffLatitude = DropoffLat,
            DropoffLongitude = DropoffLon,
            EstimatedFare = 10m
        });
        await context.SaveChangesAsync();
    }

    private async Task<decimal> EstimateFareAsync(string token, int vehicleTypeId)
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, "/api/fare/estimate", token, new
        {
            pickupLatitude = PickupLat,
            pickupLongitude = PickupLon,
            dropoffLatitude = DropoffLat,
            dropoffLongitude = DropoffLon
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var option in doc.RootElement.GetProperty("options").EnumerateArray())
        {
            if (option.GetProperty("vehicleTypeId").GetInt32() == vehicleTypeId)
            {
                return option.GetProperty("estimatedFare").GetDecimal();
            }
        }
        throw new InvalidOperationException("Vehicle type not in estimate");
    }

    // ---------- success ----------

    [Fact]
    public async Task Customer_CanRequestRide_AndItIsSaved()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await RequestAsync(customer.Token, Body());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var ride = Assert.Single(await RidesOfAsync(customer.Id));
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Equal(customer.Id, ride.CustomerId);
        Assert.Null(ride.DriverProfileUserId);
        Assert.True(ride.EstimatedFare > 0);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ride.Id, doc.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("Requested", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(ride.EstimatedFare, doc.RootElement.GetProperty("estimatedFare").GetDecimal());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RideFare_EqualsTheEstimate(int vehicleTypeId)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var estimate = await EstimateFareAsync(customer.Token, vehicleTypeId);

        var response = await RequestAsync(customer.Token, Body(vehicleTypeId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ride = Assert.Single(await RidesOfAsync(customer.Id));
        Assert.Equal(estimate, ride.EstimatedFare);
    }

    [Fact]
    public async Task ClientCannotSetFareOrStatus()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var estimate = await EstimateFareAsync(customer.Token, 1);

        var body = new
        {
            pickupLat = PickupLat,
            pickupLog = PickupLon,
            dropoffLat = DropoffLat,
            dropoffLog = DropoffLon,
            vehicleTypeId = 1,
            estimatedFare = 0.01m,
            status = "Completed",
            customerId = 999999
        };
        var response = await RequestAsync(customer.Token, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ride = Assert.Single(await RidesOfAsync(customer.Id));
        Assert.Equal(estimate, ride.EstimatedFare);
        Assert.Equal(RideStatus.Requested, ride.Status);
        Assert.Equal(customer.Id, ride.CustomerId);
    }

    // ---------- auth ----------

    [Fact]
    public async Task NoToken_Gives401()
    {
        var response = await RequestAsync(null, Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Driver)]
    [InlineData(Role.Admin)]
    public async Task NonCustomer_Gives403(Role role)
    {
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await RequestAsync(user.Token, Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- bad input ----------

    [Fact]
    public async Task MissingField_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var body = new { pickupLat = PickupLat, pickupLog = PickupLon, dropoffLat = DropoffLat, dropoffLog = DropoffLon };
        var response = await RequestAsync(customer.Token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    [Fact]
    public async Task LatitudeOutOfRange_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await RequestAsync(customer.Token, Body(pickupLat: 91));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    [Fact]
    public async Task PickupEqualsDropoff_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await RequestAsync(customer.Token,
            Body(dropoffLat: PickupLat, dropoffLog: PickupLon));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    [Fact]
    public async Task TripOver100Km_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        // Miramichi to Moncton is roughly 115 km in a straight line.
        var response = await RequestAsync(customer.Token,
            Body(dropoffLat: 46.0878, dropoffLog: -64.7782));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    [Fact]
    public async Task UnknownVehicleType_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await RequestAsync(customer.Token, Body(vehicleTypeId: 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    [Fact]
    public async Task InactiveVehicleType_Gives400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        int inactiveId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var type = new VehicleType
            {
                Name = "Inactive-" + Guid.NewGuid().ToString("N")[..8],
                Seats = 4,
                BaseFare = 1m,
                PricePerKm = 1m,
                PricePerMinute = 1m,
                MinimumFare = 1m,
                IsActive = false
            };
            context.VehicleTypes.Add(type);
            await context.SaveChangesAsync();
            inactiveId = type.Id;
        }

        var response = await RequestAsync(customer.Token, Body(vehicleTypeId: inactiveId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RidesOfAsync(customer.Id));
    }

    // ---------- one active ride ----------

    [Fact]
    public async Task SecondRequest_WhileFirstIsActive_Gives409()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var first = await RequestAsync(customer.Token, Body());
        var second = await RequestAsync(customer.Token, Body());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await RidesOfAsync(customer.Id));
    }

    [Theory]
    [InlineData(RideStatus.Requested)]
    [InlineData(RideStatus.Accepted)]
    [InlineData(RideStatus.InProgress)]
    public async Task ActiveRideInAnyActiveState_Gives409(RideStatus status)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(customer.Id, status);

        var response = await RequestAsync(customer.Token, Body());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await RidesOfAsync(customer.Id));
    }

    [Theory]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task FinishedRide_DoesNotBlockANewOne(RideStatus status)
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(customer.Id, status);

        var response = await RequestAsync(customer.Token, Body());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, (await RidesOfAsync(customer.Id)).Count);
    }

    [Fact]
    public async Task OtherCustomersActiveRide_DoesNotBlockMe()
    {
        var other = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var me = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        await InsertRideAsync(other.Id, RideStatus.Requested);

        var response = await RequestAsync(me.Token, Body());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task TwoSimultaneousRequests_OnlyOneWins()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var responses = await Task.WhenAll(
            RequestAsync(customer.Token, Body()),
            RequestAsync(customer.Token, Body()));

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => (int)c).ToArray();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, codes);
        Assert.Single(await RidesOfAsync(customer.Id));
    }
}