using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Ride
{
    public class RideResponse
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int VehicleTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double PickupLat { get; set; }
        public double PickupLog { get; set; }
        public double DropoffLat { get; set; }
        public double DropoffLog { get; set; }
        public decimal EstimatedFare { get; set; }
        public DateTime RequestedAt { get; set; }
        public decimal? FinalFare { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
