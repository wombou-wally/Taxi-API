using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class VisibleRidesTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public VisibleRidesTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Scenario(
        TestUser CustomerA, TestUser CustomerB, TestUser Driver1, TestUser Driver2, TestUser Admin,
        int Ride1, int Ride2, int Ride3);

    private static Ride NewRide(int customerId, RideStatus status, int? driverId = null) => new()
    {
        CustomerId = customerId,
        DriverProfileUserId = driverId,
        VehicleTypeId = 1,
        Status = status,
        PickupLatitude = 47.0267,
        PickupLongitude = -65.4631,
        DropoffLatitude = 47.0400,
        DropoffLongitude = -65.5000,
        EstimatedFare = 14.20m
    };

    // Ride 1: A's, Accepted, driver 1.  Ride 2: B's, Requested, no driver.  Ride 3: A's, Completed, driver 2.
    private async Task<Scenario> CreateScenarioAsync()
    {
        var a = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var b = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var d1 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var d2 = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var admin = await TestHelpers.CreateUserAsync(_factory, _client, Role.Admin);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var ride1 = NewRide(a.Id, RideStatus.Accepted, d1.Id);
        var ride2 = NewRide(b.Id, RideStatus.Requested);
        var ride3 = NewRide(a.Id, RideStatus.Completed, d2.Id);
        context.Rides.AddRange(ride1, ride2, ride3);
        await context.SaveChangesAsync();

        return new Scenario(a, b, d1, d2, admin, ride1.Id, ride2.Id, ride3.Id);
    }

    private async Task<List<int>> VisibleIdsAsync(
        int userId, Role role, Func<IQueryable<Ride>, IQueryable<Ride>>? extra = null)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var query = context.Rides.AsNoTracking().VisibleTo(userId, role);
        if (extra is not null)
        {
            query = extra(query);
        }
        return await query.Select(r => r.Id).ToListAsync();
    }

    [Fact]
    public async Task Customer_SeesOnlyOwnRides()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.CustomerA.Id, Role.Customer);

        Assert.Contains(s.Ride1, ids);
        Assert.Contains(s.Ride3, ids);
        Assert.DoesNotContain(s.Ride2, ids);
    }

    [Fact]
    public async Task OtherCustomer_SeesOnlyTheirOwnRide()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.CustomerB.Id, Role.Customer);

        Assert.Equal(new[] { s.Ride2 }, ids);
    }

    [Fact]
    public async Task Driver1_SeesOnlyRideAssignedToThem()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.Driver1.Id, Role.Driver);

        Assert.Equal(new[] { s.Ride1 }, ids);
    }

    [Fact]
    public async Task Driver2_SeesOnlyRideAssignedToThem()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.Driver2.Id, Role.Driver);

        Assert.Equal(new[] { s.Ride3 }, ids);
    }

    [Fact]
    public async Task Drivers_NeverSee_TheUnassignedRide()
    {
        var s = await CreateScenarioAsync();

        Assert.DoesNotContain(s.Ride2, await VisibleIdsAsync(s.Driver1.Id, Role.Driver));
        Assert.DoesNotContain(s.Ride2, await VisibleIdsAsync(s.Driver2.Id, Role.Driver));
    }

    [Fact]
    public async Task Admin_SeesAllRides()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.Admin.Id, Role.Admin);

        Assert.Contains(s.Ride1, ids);
        Assert.Contains(s.Ride2, ids);
        Assert.Contains(s.Ride3, ids);
    }

    [Fact]
    public async Task CustomerWithNoRides_SeesNothing()
    {
        var lonely = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var ids = await VisibleIdsAsync(lonely.Id, Role.Customer);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task UnknownRole_SeesNothing()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.CustomerA.Id, (Role)99);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task AskingForSomeoneElsesRide_ById_FindsNothing()
    {
        var s = await CreateScenarioAsync();

        var ids = await VisibleIdsAsync(s.CustomerB.Id, Role.Customer, q => q.Where(r => r.Id == s.Ride1));

        Assert.Empty(ids);
    }

    [Fact]
    public async Task VisibleQuery_ComposesWithFurtherFilters()
    {
        var s = await CreateScenarioAsync();

        var completed = await VisibleIdsAsync(s.CustomerA.Id, Role.Customer,
            q => q.Where(r => r.Status == RideStatus.Completed));
        var accepted = await VisibleIdsAsync(s.CustomerA.Id, Role.Customer,
            q => q.Where(r => r.Status == RideStatus.Accepted));

        Assert.Equal(new[] { s.Ride3 }, completed);
        Assert.Equal(new[] { s.Ride1 }, accepted);
    }
}