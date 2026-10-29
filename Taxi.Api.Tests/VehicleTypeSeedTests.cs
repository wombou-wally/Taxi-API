using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taxi.Api.Data;

namespace Taxi.Api.Tests;

public class VehicleTypeSeedTests : IClassFixture<TaxiApiFactory>
{
    private readonly TaxiApiFactory _factory;

    public VehicleTypeSeedTests(TaxiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task VehicleTypes_AreSeeded_WithPositivePrices()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TaxiDbContext>();

        var types = await context.VehicleTypes.AsNoTracking().ToListAsync();

        Assert.Contains(types, t => t.Name == "Standard");
        Assert.Contains(types, t => t.Name == "Comfort");
        Assert.Contains(types, t => t.Name == "XL");

        Assert.All(types, t =>
        {
            Assert.True(t.Seats > 0);
            Assert.True(t.BaseFare > 0);
            Assert.True(t.PricePerKm > 0);
            Assert.True(t.PricePerMinute > 0);
            Assert.True(t.MinimumFare >= t.BaseFare);
        });
    }
}