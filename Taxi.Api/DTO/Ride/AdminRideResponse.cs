using Taxi.Api.Models;

namespace Taxi.Api.DTO.Ride
{
    public class AdminRideResponse
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int? DriverId { get; set; }
        public int? VehicleId { get; set; }
        public int VehicleTypeId { get; set; }
        public string VehicleTypeName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public double PickupLat { get; set; }
        public double PickupLog { get; set; }
        public double DropoffLat { get; set; }
        public double DropoffLog { get; set; }
        public decimal EstimatedFare { get; set; }
        public decimal? FinalFare { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? CancelledBy { get; set; }
        public string? CancelReason { get; set; }
    }
}
