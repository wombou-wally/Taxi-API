using Taxi.Api.Services;

namespace Taxi.Api.Tests;

public class HaversineDistanceServiceTests
{
    private readonly HaversineDistanceService _service = new();

    [Theory]
    [InlineData(0, 0, 0, 1, 111.19, 0.1)]            // one degree of longitude on the equator
    [InlineData(0, 0, 1, 0, 111.19, 0.1)]            // one degree of latitude
    [InlineData(0, 0, 0, 90, 10007.5, 1)]            // quarter of the equator
    [InlineData(0, 0, 0, 180, 20015.1, 1)]           // opposite sides of the Earth
    [InlineData(90, 0, -90, 0, 20015.1, 1)]          // pole to pole
    [InlineData(51.5074, -0.1278, 48.8566, 2.3522, 343.5, 1)]   // London to Paris
    [InlineData(47.0267, -65.4631, 45.9636, -66.6431, 148.8, 2)] // Miramichi to Fredericton
    [InlineData(47.0267, -65.4631, 47.0267, -65.4631, 0, 0.001)] // same point
    public void DistanceKm_ReturnsKnownDistances(
        double lat1, double lon1, double lat2, double lon2, double expectedKm, double toleranceKm)
    {
        var actual = _service.DistanceKm(lat1, lon1, lat2, lon2);

        Assert.InRange(actual, expectedKm - toleranceKm, expectedKm + toleranceKm);
    }

    [Fact]
    public void DistanceKm_OppositePoints_IsNotNaN()
    {
        var actual = _service.DistanceKm(0, 0, 0, 180);

        Assert.False(double.IsNaN(actual));
    }

    [Fact]
    public void DistanceKm_IsSymmetric()
    {
        var there = _service.DistanceKm(47.0267, -65.4631, 45.9636, -66.6431);
        var back = _service.DistanceKm(45.9636, -66.6431, 47.0267, -65.4631);

        Assert.Equal(there, back, 6);
    }

    [Theory]
    [InlineData(91, 0, 0, 0)]
    [InlineData(-91, 0, 0, 0)]
    [InlineData(0, 181, 0, 0)]
    [InlineData(0, -181, 0, 0)]
    [InlineData(0, 0, 91, 0)]
    [InlineData(0, 0, -91, 0)]
    [InlineData(0, 0, 0, 181)]
    [InlineData(0, 0, 0, -181)]
    public void DistanceKm_OutOfRangeCoordinates_Throws(double lat1, double lon1, double lat2, double lon2)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.DistanceKm(lat1, lon1, lat2, lon2));
    }

    [Fact]
    public void DistanceKm_BoundaryValues_DoNotThrow()
    {
        var actual = _service.DistanceKm(90, 180, -90, -180);

        Assert.False(double.IsNaN(actual));
    }
}