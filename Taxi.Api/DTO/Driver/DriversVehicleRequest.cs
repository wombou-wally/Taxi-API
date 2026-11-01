using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Taxi.Api.DTO.Driver
{
    public class DriversVehicleRequest
    {
        [MaxLength(100)][Required]public required string Model { get; set; }
        [MaxLength(20)][Required]public required string PlateNumber { get; set; }
        [MaxLength(50)][Required]public required string InsuranceNumber { get; set; }
        [Required]public required DateOnly InsuranceExpiryDate { get; set; }
        public int VehicleTypeId { get; set; }
    }
}
