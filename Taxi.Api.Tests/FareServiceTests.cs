using Taxi.Api.Models;
using Taxi.Api.Services;

namespace Taxi.Api.Tests;

public class FareServiceTests
{
    private readonly FareService _service = new();

    private static VehicleType Standard() => new()
    {
        Name = "Standard",
        Seats = 4,
        BaseFare = 4.00m,
        PricePerKm = 1.80m,
        PricePerMinute = 0.35m,
        MinimumFare = 7.00m,
        IsActive = true
    };

    private static VehicleType Comfort() => new()
    {
        Name = "Comfort",
        Seats = 4,
        BaseFare = 5.00m,
        PricePerKm = 2.20m,
        PricePerMinute = 0.45m,
        MinimumFare = 9.00m,
        IsActive = true
    };

    private static VehicleType Xl() => new()
    {
        Name = "XL",
        Seats = 6,
        BaseFare = 6.50m,
        PricePerKm = 2.80m,
        PricePerMinute = 0.55m,
        MinimumFare = 12.00m,
        IsActive = true
    };

    [Fact]
    public void Standard_5Km_10Minutes_IsBasePlusDistancePlusTime()
    {
        // road distance 6.5 km: 4.00 + 1.80 * 6.5 + 0.35 * 10 = 19.20
        var fare = _service.CalculateFare(Standard(), 5, 10);

        Assert.Equal(19.20m, fare);
    }

    [Fact]
    public void Comfort_5Km_10Minutes()
    {
        // 5.00 + 2.20 * 6.5 + 0.45 * 10 = 23.80
        var fare = _service.CalculateFare(Comfort(), 5, 10);

        Assert.Equal(23.80m, fare);
    }

    [Fact]
    public void Xl_5Km_10Minutes()
    {
        // 6.50 + 2.80 * 6.5 + 0.55 * 10 = 30.20
        var fare = _service.CalculateFare(Xl(), 5, 10);

        Assert.Equal(30.20m, fare);
    }

    [Fact]
    public void LongTrip_Standard_50Km_60Minutes()
    {
        // road distance 65 km: 4.00 + 117.00 + 21.00 = 142.00
        var fare = _service.CalculateFare(Standard(), 50, 60);

        Assert.Equal(142.00m, fare);
    }

    [Fact]
    public void ShortTrip_BelowMinimum_ChargesMinimumFare()
    {
        // 4.00 + 0.234 + 0.35 = 4.584, below the 7.00 minimum
        var fare = _service.CalculateFare(Standard(), 0.1, 1);

        Assert.Equal(7.00m, fare);
    }

    [Fact]
    public void ZeroDistanceAndZeroMinutes_ChargesMinimumFare()
    {
        var fare = _service.CalculateFare(Standard(), 0, 0);

        Assert.Equal(7.00m, fare);
    }

    [Fact]
    public void TripJustAboveMinimum_IsNotRaisedToMinimum()
    {
        // road distance 1.3 km: 4.00 + 2.34 + 1.05 = 7.39
        var fare = _service.CalculateFare(Standard(), 1, 3);

        Assert.Equal(7.39m, fare);
    }

    [Fact]
    public void Rounding_HalfCentRoundsAwayFromZero()
    {
        var type = new VehicleType { BaseFare = 2.345m, PricePerKm = 0, PricePerMinute = 0, MinimumFare = 0 };

        var fare = _service.CalculateFare(type, 0, 0);

        Assert.Equal(2.35m, fare);   // banker's rounding would give 2.34
    }

    [Fact]
    public void Rounding_HappensOnlyOnce_AtTheEnd()
    {
        // 0.004 + 0.004 = 0.008 rounds to 0.01. Rounding each part first would give 0.00.
        var type = new VehicleType { BaseFare = 0.004m, PricePerKm = 0, PricePerMinute = 0.004m, MinimumFare = 0 };

        var fare = _service.CalculateFare(type, 0, 1);

        Assert.Equal(0.01m, fare);
    }

    [Fact]
    public void LongerTrip_CostsMoreThanShorterTrip()
    {
        var shorter = _service.CalculateFare(Standard(), 5, 10);
        var longer = _service.CalculateFare(Standard(), 10, 20);

        Assert.True(longer > shorter);
    }

    [Fact]
    public void NegativeDistance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateFare(Standard(), -1, 10));
    }

    [Fact]
    public void NegativeMinutes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateFare(Standard(), 5, -1));
    }

    [Fact]
    public void NaNDistance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateFare(Standard(), double.NaN, 10));
    }

    [Fact]
    public void InfiniteMinutes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateFare(Standard(), 5, double.PositiveInfinity));
    }

    [Fact]
    public void NullVehicleType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.CalculateFare(null!, 5, 10));
    }
}