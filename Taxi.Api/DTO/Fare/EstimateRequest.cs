using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Fare
{
    public class EstimateRequest
    {
        [Range(-90, 90)] public required double PickupLatitude { get; set; }
        [Range(-180, 180)] public required double PickupLongitude { get; set; }
        [Range(-90, 90)] public required double DropoffLatitude { get; set; }
        [Range(-180, 180)] public required double DropoffLongitude { get; set; }

    }
}
