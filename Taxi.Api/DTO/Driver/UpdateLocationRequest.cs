using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Driver
{
    public class UpdateLocationRequest
    {
        [Range(-90, 90)] public required double Latitude { get; set; }
        [Range(-180, 180)] public required double Longitude { get; set; }
    }
}
