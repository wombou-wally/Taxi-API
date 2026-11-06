namespace Taxi.Api.DTO.Ride
{
    public class AvailableRideResponse
    {
        public int Id { get; set; }
        public string VehicleTypeName { get; set; } = string.Empty;
        public double PickupLat { get; set; }
        public double PickupLog { get; set; }
        public double DropoffLat { get; set; }
        public double DropoffLog { get; set; }
        public decimal EstimatedFare { get; set; }
        public double DistanceToPickupKm { get; set; }
        public DateTime RequestedAt { get; set; }
    }
}