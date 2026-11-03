using Taxi.Api.Models;

namespace Taxi.Api.Services
{
    public class FareService : IFareService
    {
        
        private const decimal RoadDistanceFactor = 1.3m;

        public decimal CalculateFare(VehicleType vehicleType, double straightLineKm, double minutes)
        {
            ArgumentNullException.ThrowIfNull(vehicleType);

            if (!double.IsFinite(straightLineKm) || straightLineKm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(straightLineKm), "Distance must be zero or more.");
            }
            if (!double.IsFinite(minutes) || minutes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minutes), "Minutes must be zero or more.");
            }

            decimal roadKm = (decimal)straightLineKm * RoadDistanceFactor;
            decimal rideMinutes = (decimal)minutes;

            decimal distanceCharge = vehicleType.PricePerKm * roadKm;
            decimal timeCharge = vehicleType.PricePerMinute * rideMinutes;

            decimal fare = vehicleType.BaseFare + distanceCharge + timeCharge;

            if (fare < vehicleType.MinimumFare)
            {
                fare = vehicleType.MinimumFare;
            }

            // Round once, at the very end.
            return Math.Round(fare, 2, MidpointRounding.AwayFromZero);
        }
    }
}