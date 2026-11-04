using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Fare;
using Taxi.Api.Models;

namespace Taxi.Api.Tests;

public class FareEstimateTests : IClassFixture<TaxiApiFactory>
{
    private const string Url = "api/fare/estimate";
    private readonly TaxiApiFactory _factory;
    private readonly HttpClient _client;

    public FareEstimateTests(TaxiApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // Short trip inside Miramichi: about 3.2 km straight line.
    private static object ShortTrip() => new
    {
        PickupLatitude = 47.0267,
        PickupLongitude = -65.4631,
        DropoffLatitude = 47.0400,
        DropoffLongitude = -65.5000
    };

    private async Task<EstimateResponse> GetEstimateAsync(string token, object body)
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, token, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var estimate = await response.Content.ReadFromJsonAsync<EstimateResponse>();
        Assert.NotNull(estimate);
        return estimate;
    }

    private static EstimateOption Option(EstimateResponse estimate, string name) =>
        Assert.Single(estimate.Options, o => o.Name == name);

    [Fact]
    public async Task Estimate_NoToken_Returns401()
    {
        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, null, ShortTrip());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Driver)]
    [InlineData(Role.Admin)]
    public async Task Estimate_AnyLoggedInRole_Returns200(Role role)
    {
        var user = await TestHelpers.CreateUserAsync(_factory, _client, role);

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, user.Token, ShortTrip());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Estimate_ShortTrip_ReturnsDistanceMinutesAndSeededTypes()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var estimate = await GetEstimateAsync(customer.Token, ShortTrip());

        Assert.InRange(estimate.StraightLineKm, 3.1, 3.3);
        Assert.Equal(8, estimate.EstimatedMinutes);

        var standard = Option(estimate, "Standard");
        Option(estimate, "Comfort");
        Option(estimate, "XL");

        Assert.Equal(1, standard.VehicleTypeId);
        Assert.Equal(4, standard.Seats);
        Assert.InRange(standard.EstimatedFare, 14.0m, 14.5m);
    }

    [Fact]
    public async Task Estimate_FaresRiseFromStandardToComfortToXl_AndCheapestComesFirst()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var estimate = await GetEstimateAsync(customer.Token, ShortTrip());

        var standard = Option(estimate, "Standard");
        var comfort = Option(estimate, "Comfort");
        var xl = Option(estimate, "XL");

        Assert.True(standard.EstimatedFare < comfort.EstimatedFare);
        Assert.True(comfort.EstimatedFare < xl.EstimatedFare);

        var names = estimate.Options.Select(o => o.Name).ToList();
        Assert.True(names.IndexOf("Standard") < names.IndexOf("Comfort"));
        Assert.True(names.IndexOf("Comfort") < names.IndexOf("XL"));
    }

    [Fact]
    public async Task Estimate_VeryShortTrip_ChargesTheMinimumFares()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var body = new
        {
            PickupLatitude = 47.0267,
            PickupLongitude = -65.4631,
            DropoffLatitude = 47.0268,
            DropoffLongitude = -65.4631
        };

        var estimate = await GetEstimateAsync(customer.Token, body);

        Assert.Equal(7.00m, Option(estimate, "Standard").EstimatedFare);
        Assert.Equal(9.00m, Option(estimate, "Comfort").EstimatedFare);
        Assert.Equal(12.00m, Option(estimate, "XL").EstimatedFare);
    }

    [Fact]
    public async Task Estimate_InactiveType_IsLeftOut_ActiveNewTypeIsIncluded()
    {
        var inactiveName = $"Off{Guid.NewGuid():N}";
        var activeName = $"On{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();
            context.VehicleTypes.Add(new VehicleType
            {
                Name = inactiveName,
                Seats = 4,
                BaseFare = 3.00m,
                PricePerKm = 1.00m,
                PricePerMinute = 0.20m,
                MinimumFare = 5.00m,
                IsActive = false
            });
            context.VehicleTypes.Add(new VehicleType
            {
                Name = activeName,
                Seats = 4,
                BaseFare = 3.00m,
                PricePerKm = 1.00m,
                PricePerMinute = 0.20m,
                MinimumFare = 5.00m,
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var estimate = await GetEstimateAsync(customer.Token, ShortTrip());

        Assert.DoesNotContain(estimate.Options, o => o.Name == inactiveName);
        Assert.Contains(estimate.Options, o => o.Name == activeName);
    }

    [Fact]
    public async Task Estimate_SameRequestTwice_GivesSameAnswer()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);

        var first = await GetEstimateAsync(customer.Token, ShortTrip());
        var second = await GetEstimateAsync(customer.Token, ShortTrip());

        Assert.Equal(first.StraightLineKm, second.StraightLineKm);
        Assert.Equal(first.EstimatedMinutes, second.EstimatedMinutes);
        Assert.Equal(Option(first, "Standard").EstimatedFare, Option(second, "Standard").EstimatedFare);
    }

    [Fact]
    public async Task Estimate_LatitudeOutOfRange_Returns400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var body = new
        {
            PickupLatitude = 91.0,
            PickupLongitude = -65.4631,
            DropoffLatitude = 47.04,
            DropoffLongitude = -65.5
        };

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, customer.Token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Estimate_MissingField_Returns400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var body = new
        {
            PickupLatitude = 47.0267,
            PickupLongitude = -65.4631,
            DropoffLatitude = 47.04
        };

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, customer.Token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Estimate_TripLongerThanMaximum_Returns400()
    {
        var customer = await TestHelpers.CreateUserAsync(_factory, _client, Role.Customer);
        var body = new
        {
            PickupLatitude = 47.0267,
            PickupLongitude = -65.4631,
            DropoffLatitude = 45.9636,     // Fredericton, about 149 km away
            DropoffLongitude = -66.6431
        };

        var response = await TestHelpers.SendAsync(_client, HttpMethod.Post, Url, customer.Token, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}