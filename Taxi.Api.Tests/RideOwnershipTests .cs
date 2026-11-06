using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

// One ride: owned by customer A, accepted by driver 1.
// Every kind of viewer asks for it by id and through the list.
public class RideOwnershipTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public RideOwnershipTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("owner", HttpStatusCode.OK)]
    [InlineData("otherCustomer", HttpStatusCode.NotFound)]
    [InlineData("assignedDriver", HttpStatusCode.OK)]
    [InlineData("otherDriver", HttpStatusCode.NotFound)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    public async Task Viewer_SeesTheRide_OnlyWhenAllowed(string viewer, HttpStatusCode expected)
    {
        var owner = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var otherCustomer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var assignedDriver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var otherDriver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);

        int rideId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var ride = new Ride
            {
                CustomerId = owner.Id,
                DriverProfileUserId = assignedDriver.Id,
                VehicleTypeId = 1,
                Status = RideStatus.Accepted,
                PickupLatitude = 47.0267,
                PickupLongitude = -65.4631,
                DropoffLatitude = 47.0400,
                DropoffLongitude = -65.5000,
                EstimatedFare = 14.20m
            };
            context.Rides.Add(ride);
            await context.SaveChangesAsync();
            rideId = ride.Id;
        }

        string? token = viewer switch
        {
            "owner" => owner.Token,
            "otherCustomer" => otherCustomer.Token,
            "assignedDriver" => assignedDriver.Token,
            "otherDriver" => otherDriver.Token,
            "admin" => admin.Token,
            _ => null
        };

        // By id.
        var byId = await TestHelpers.SendAsync(_client, HttpMethod.Get, $"/api/rides/{rideId}", token);
        Assert.Equal(expected, byId.StatusCode);

        // Through the list: appears if and only if the by-id call succeeded.
        var list = await TestHelpers.SendAsync(_client, HttpMethod.Get, "/api/rides?limit=50", token);
        if (expected == HttpStatusCode.Unauthorized)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();

        if (expected == HttpStatusCode.OK)
        {
            Assert.Contains(rideId, ids);
        }
        else
        {
            Assert.DoesNotContain(rideId, ids);
        }
    }
}