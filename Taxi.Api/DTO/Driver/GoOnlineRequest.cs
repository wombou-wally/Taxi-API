using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Driver
{
    public class GoOnlineRequest
    {
        [Range(1, int.MaxValue)]public int VehicleId { get; set; }
    }
}
