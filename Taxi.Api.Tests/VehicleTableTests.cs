using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class VehicleTableTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;

    public VehicleTableTests(TaxiApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<int> CreateDriverAsync(TaxiDbContext context, IPasswordHasher<User> hasher)
    {
        var user = new User
        {
            Email = $"driver_{Guid.NewGuid():N}@test.com",
            FullName = "Vehicle Test Driver",
            Phone = "5065550100",
            Role = Role.Driver,
            PasswordHash = "",
            DriverProfile = new DriverProfile { LicenceNumber = "LIC-VEH-1" }
        };
        user.PasswordHash = hasher.HashPassword(user, "Password123!");

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Vehicle_CanBeSaved_ForDriverAndType()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var driverId = await CreateDriverAsync(context, hasher);
        var plate = $"P{Guid.NewGuid():N}"[..15];

        context.Vehicles.Add(new Vehicle
        {
            Model = "Toyota Corolla",
            PlateNumber = plate,
            InsuranceNumber = "INS-1",
            InsuranceExpiryDate = new DateOnly(2030, 1, 1),
            DriverProfileUserId = driverId,
            VehicleTypeId = 1
        });
        await context.SaveChangesAsync();

        var saved = await context.Vehicles.AsNoTracking().SingleAsync(v => v.PlateNumber == plate);
        Assert.Equal(driverId, saved.DriverProfileUserId);
        Assert.Equal(1, saved.VehicleTypeId);
    }

    [Fact]
    public async Task Vehicle_DuplicatePlate_IsRejected()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var driverId = await CreateDriverAsync(context, hasher);
        var plate = $"D{Guid.NewGuid():N}"[..15];

        Vehicle NewVehicle() => new()
        {
            Model = "Honda Civic",
            PlateNumber = plate,
            InsuranceNumber = "INS-2",
            InsuranceExpiryDate = new DateOnly(2030, 1, 1),
            DriverProfileUserId = driverId,
            VehicleTypeId = 1
        };

        context.Vehicles.Add(NewVehicle());
        await context.SaveChangesAsync();

        context.Vehicles.Add(NewVehicle());
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}