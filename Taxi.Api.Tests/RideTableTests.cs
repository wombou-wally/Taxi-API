using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class RideTableTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public RideTableTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<TestUser> NewCustomerAsync() => TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

    private static Ride NewRide(int customerId, RideStatus status = RideStatus.Requested, int vehicleTypeId = 1) => new()
    {
        CustomerId = customerId,
        VehicleTypeId = vehicleTypeId,
        Status = status,
        PickupLatitude = 47.0267,
        PickupLongitude = -65.4631,
        DropoffLatitude = 47.0400,
        DropoffLongitude = -65.5000,
        EstimatedFare = 14.20m
    };

    [Fact]
    public async Task Ride_SavesAndReadsBack_WithDefaults()
    {
        var customer = await NewCustomerAsync();

        int rideId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var ride = NewRide(customer.Id);
            context.Rides.Add(ride);
            await context.SaveChangesAsync();
            rideId = ride.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var saved = await context.Rides.AsNoTracking().SingleAsync(r => r.Id == rideId);

            Assert.Equal(RideStatus.Requested, saved.Status);
            Assert.Equal(customer.Id, saved.CustomerId);
            Assert.Equal(14.20m, saved.EstimatedFare);
            Assert.True((DateTime.UtcNow - saved.RequestedAt).TotalSeconds < 60);
            Assert.Null(saved.DriverProfileUserId);
            Assert.Null(saved.VehicleId);
            Assert.Null(saved.FinalFare);
            Assert.Null(saved.AcceptedAt);
            Assert.Null(saved.CancelledBy);
        }
    }

    [Fact]
    public async Task SecondActiveRide_ForSameCustomer_IsRejected()
    {
        var customer = await NewCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        context.Rides.Add(NewRide(customer.Id));
        await context.SaveChangesAsync();

        context.Rides.Add(NewRide(customer.Id, RideStatus.Accepted));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(RideStatus.Completed)]
    [InlineData(RideStatus.Cancelled)]
    public async Task NewRide_IsAllowed_WhenPreviousRideIsFinished(RideStatus finished)
    {
        var customer = await NewCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        context.Rides.Add(NewRide(customer.Id, finished));
        context.Rides.Add(NewRide(customer.Id, finished));
        await context.SaveChangesAsync();

        context.Rides.Add(NewRide(customer.Id));
        await context.SaveChangesAsync();

        var count = await context.Rides.CountAsync(r => r.CustomerId == customer.Id);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task TwoDifferentCustomers_CanEachHaveAnActiveRide()
    {
        var first = await NewCustomerAsync();
        var second = await NewCustomerAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        context.Rides.Add(NewRide(first.Id));
        context.Rides.Add(NewRide(second.Id));
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Rides.CountAsync(r => r.CustomerId == first.Id));
        Assert.Equal(1, await context.Rides.CountAsync(r => r.CustomerId == second.Id));
    }

    [Fact]
    public async Task DeletingVehicle_KeepsRide_AndClearsVehicleLink()
    {
        var customer = await NewCustomerAsync();
        var driver = await TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);
        var vehicleId = await TestHelpers.CreateVehicleAsync(_factory, driver.Id);

        int rideId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var ride = NewRide(customer.Id, RideStatus.Completed);
            ride.DriverProfileUserId = driver.Id;
            ride.VehicleId = vehicleId;
            context.Rides.Add(ride);
            await context.SaveChangesAsync();
            rideId = ride.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var vehicle = await context.Vehicles.SingleAsync(v => v.Id == vehicleId);
            context.Vehicles.Remove(vehicle);
            await context.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            var ride = await context.Rides.AsNoTracking().SingleAsync(r => r.Id == rideId);

            Assert.Null(ride.VehicleId);
            Assert.Equal(driver.Id, ride.DriverProfileUserId);
        }
    }

    [Fact]
    public async Task DeletingVehicleType_UsedByARide_IsRefused()
    {
        var customer = await NewCustomerAsync();
        int typeId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

            var type = new VehicleType
            {
                Name = $"R{Guid.NewGuid():N}",
                Seats = 4,
                BaseFare = 3.00m,
                PricePerKm = 1.00m,
                PricePerMinute = 0.20m,
                MinimumFare = 5.00m,
                IsActive = true
            };
            context.VehicleTypes.Add(type);
            await context.SaveChangesAsync();
            typeId = type.Id;

            context.Rides.Add(NewRide(customer.Id, RideStatus.Completed, typeId));
            await context.SaveChangesAsync();
        }

        // A fresh context: the ride is not tracked, so the DELETE reaches the database.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

            var type = await context.VehicleTypes.SingleAsync(t => t.Id == typeId);
            context.VehicleTypes.Remove(type);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task DeletingCustomer_WhoHasRides_IsRefused()
    {
        var customer = await NewCustomerAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

            context.Rides.Add(NewRide(customer.Id, RideStatus.Completed));
            await context.SaveChangesAsync();
        }

        // A fresh context: the ride is not tracked, so the DELETE reaches the database.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

            var user = await context.Users.SingleAsync(u => u.Id == customer.Id);
            context.Users.Remove(user);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }
}