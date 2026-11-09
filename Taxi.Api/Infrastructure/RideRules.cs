namespace Taxi.Api.Infrastructure
{
    public static class RideRules
    {
        public const double AverageSpeedKmh = 25;
        public const double MinDistanceKm = 0.1;
        public const double MaxDistanceKm = 100.0;
        public const decimal MaxFareMultiplier = 2m;

        // Estimated trip time for a distance, rounded up, at least 1 minute.
        public static double EstimatedMinutes(double distanceKm) =>
            Math.Max(1, Math.Ceiling(distanceKm / AverageSpeedKmh * 60));

        // Real elapsed time, rounded up, at least 1 minute.
        public static double ElapsedMinutes(DateTime startUtc, DateTime endUtc) =>
            Math.Max(1, Math.Ceiling((endUtc - startUtc).TotalMinutes));
    }
}