using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Driver;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class DriverVehicleTests : IClassFixture<TaxiApiFactory>
{
    private const string Url = "api/driver/vehicle";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public DriverVehicleTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static string NewPlate() => ("T" + Guid.NewGuid().ToString("N"))[..12].ToUpperInvariant();

    private static object Body(string plate, int typeId = 1, string model = "Toyota Corolla", DateOnly? expiry = null) => new
    {
        Model = model,
        PlateNumber = plate,
        InsuranceNumber = "INS-1",
        InsuranceExpiryDate = expiry ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
        VehicleTypeId = typeId
    };

    private Task<TestUser> NewDriverAsync() => TestHelpers.CreateUserAsync(_factory, _client, Role.Driver);

    private async Task<int> CreateVehicleAsync(TestUser driver, string plate)
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, driver.Token, Body(plate));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<DriversVehicleResponse>();
        Assert.NotNull(created);
        return created.Id;
    }

    private async Task<Vehicle?> GetVehicleFromDbAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
        return await context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id);
    }

    [Fact]
    public async Task Create_NoToken_Returns401()
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, null, Body(NewPlate()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_CustomerToken_Returns403()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, customer.Token, Body(NewPlate()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_AndSavesOwnerAndUppercasePlate()
    {
        var driver = await NewDriverAsync();
        var plate = NewPlate();

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, driver.Token, Body(plate.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<DriversVehicleResponse>();
        Assert.NotNull(created);

        var saved = await GetVehicleFromDbAsync(created.Id);
        Assert.NotNull(saved);
        Assert.Equal(driver.Id, saved.DriverProfileUserId);
        Assert.Equal(plate, saved.PlateNumber);
    }

    [Fact]
    public async Task Create_DuplicatePlate_DifferentCase_Returns409()
    {
        var driver = await NewDriverAsync();
        var plate = NewPlate();
        await CreateVehicleAsync(driver, plate);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, driver.Token, Body(plate.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownVehicleType_Returns400()
    {
        var driver = await NewDriverAsync();

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, driver.Token, Body(NewPlate(), typeId: 9999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_PastInsuranceDate_Returns400()
    {
        var driver = await NewDriverAsync();
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, driver.Token, Body(NewPlate(), expiry: past));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnVehicles()
    {
        var driverA = await NewDriverAsync();
        var driverB = await NewDriverAsync();
        var plateA = NewPlate();
        var plateB = NewPlate();
        await CreateVehicleAsync(driverA, plateA);
        await CreateVehicleAsync(driverB, plateB);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Get, Url, driverA.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<DriversVehicleResponse>>();
        Assert.NotNull(list);
        Assert.Contains(list, v => v.PlateNumber == plateA);
        Assert.DoesNotContain(list, v => v.PlateNumber == plateB);
    }

    [Fact]
    public async Task Update_OwnVehicle_KeepingSamePlate_Returns200()
    {
        var driver = await NewDriverAsync();
        var plate = NewPlate();
        var id = await CreateVehicleAsync(driver, plate);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Put, $"{Url}/{id}", driver.Token,
            Body(plate, model: "Honda Civic"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Honda Civic", (await GetVehicleFromDbAsync(id))!.Model);
    }

    [Fact]
    public async Task Update_OtherDriversVehicle_Returns404_AndNothingChanges()
    {
        var owner = await NewDriverAsync();
        var intruder = await NewDriverAsync();
        var plate = NewPlate();
        var id = await CreateVehicleAsync(owner, plate);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Put, $"{Url}/{id}", intruder.Token,
            Body(NewPlate(), model: "Hacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var saved = await GetVehicleFromDbAsync(id);
        Assert.Equal("Toyota Corolla", saved!.Model);
        Assert.Equal(plate, saved.PlateNumber);
    }

    [Fact]
    public async Task Update_PlateOfAnotherVehicle_Returns409()
    {
        var driver = await NewDriverAsync();
        var plateOne = NewPlate();
        var plateTwo = NewPlate();
        await CreateVehicleAsync(driver, plateOne);
        var secondId = await CreateVehicleAsync(driver, plateTwo);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Put, $"{Url}/{secondId}", driver.Token, Body(plateOne));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnVehicle_Returns204_AndRowIsGone()
    {
        var driver = await NewDriverAsync();
        var id = await CreateVehicleAsync(driver, NewPlate());

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Delete, $"{Url}/{id}", driver.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await GetVehicleFromDbAsync(id));
    }

    [Fact]
    public async Task Delete_OtherDriversVehicle_Returns404_AndRowStays()
    {
        var owner = await NewDriverAsync();
        var intruder = await NewDriverAsync();
        var id = await CreateVehicleAsync(owner, NewPlate());

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Delete, $"{Url}/{id}", intruder.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(await GetVehicleFromDbAsync(id));
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        var driver = await NewDriverAsync();

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Delete, $"{Url}/{int.MaxValue}", driver.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}