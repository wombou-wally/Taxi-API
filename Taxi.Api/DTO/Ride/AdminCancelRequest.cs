using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Ride
{
    public class AdminCancelRequest
    {
        [Required][StringLength(200, MinimumLength = 1)] public required string Reason { get; set; }
    }
}
