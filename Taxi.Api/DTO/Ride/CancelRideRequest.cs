using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Ride
{
    public class CancelRideRequest
    {
        [StringLength(200)]
        public string? Reason { get; set; }
    }
}