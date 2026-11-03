using Taxi.Api.Models;

namespace Taxi.Api.Services
{
    public interface IFareService
    {
        //distance is the straight-line distance in kilometres and the result is in dollars.
        decimal CalculateFare(VehicleType vehicleType, double straightLineKm, double minutes);
    }
}
