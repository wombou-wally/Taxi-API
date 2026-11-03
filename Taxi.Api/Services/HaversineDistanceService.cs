namespace Taxi.Api.Services
{
    public class HaversineDistanceService : IDistanceService
    {
        private const double EarthRadiusKm = 6371.0;

        private static double ConvertDegreeToRadian(double degrees)
        {
            //degrees = Math.PI / 180;
            return degrees * Math.PI / 180;
        }
        private static bool IsInRange(double lat1, double long1)
        {
            if ((lat1 >= -90 && lat1 <= 90) && (long1 >= -180 && long1 <= 180))
            {
                return true;
            }
            return false;
        }
        public double DistanceKm(double firstLat, double firstLong, double secondLat , double secondLong)
        {
            if (!IsInRange(firstLat, firstLong) || !IsInRange(secondLat, secondLong))
            {
                throw new ArgumentOutOfRangeException(nameof(firstLat),$"latitude must be between -90 and 90 and longitude between -180 and 180.");
            }
            firstLat = ConvertDegreeToRadian(firstLat);
            firstLong = ConvertDegreeToRadian(firstLong);
            secondLat = ConvertDegreeToRadian(secondLat);
            secondLong = ConvertDegreeToRadian(secondLong);

            double diffLat = secondLat - firstLat;
            double diffLong = secondLong - firstLong;

            double a = Math.Pow(((Math.Sin(diffLat / 2.0)) ), 2) + ((Math.Cos(firstLat)) * (Math.Cos(secondLat)) * (Math.Pow(Math.Sin(diffLong / 2.0), 2)));
            a = Math.Clamp(a, 0, 1);

            double c = (2 * Math.Atan2((Math.Sqrt(a)), Math.Sqrt(1 - a)));

            return EarthRadiusKm * c;
        }
    }
}
