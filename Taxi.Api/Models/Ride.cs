namespace Taxi.Api.Models
{
    public class Ride
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public User Customer { get; set; } = null!;
        public int? DriverProfileUserId { get; set; }
        public DriverProfile? Driver { get; set; }
        public int? VehicleId { get; set; }
        public Vehicle? Vehicle { get; set; }
        public  int VehicleTypeId { get; set; } 
        public VehicleType VehicleType { get; set; } = null!;
        public RideStatus Status { get; set; } = RideStatus.Requested;
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public double DropoffLatitude { get; set; }
        public double DropoffLongitude { get; set; }
        public decimal EstimatedFare { get; set; }
        public decimal? FinalFare { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcceptedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Role? CancelledBy { get; set; }
        public string? CancelReason { get; set; }
    }
}
