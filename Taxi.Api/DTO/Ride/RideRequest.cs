using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Ride
{
    public class RideRequest
    {
        [Range(-90, 90)] public required double PickupLat { get; set; }
        [Range(-180, 180)] public required double PickupLog { get; set; }
        [Range(-90, 90)] public required double DropoffLat { get; set; }
        [Range(-180, 180)] public required double DropoffLog { get; set; }
        public required int VehicleTypeId { get; set; }
    }
}
