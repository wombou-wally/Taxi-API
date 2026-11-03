namespace Taxi.Api.Services
{
    public interface IDistanceService
    {
        double DistanceKm(double firstLat, double firstLong, double secondLat, double secondLong);
    }
}
