using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Vehicles
{
    public class VehicleRequest
    {
        [Range(typeof(decimal), "0.01", "1000")] public decimal BaseFare { get; set; }
        [Range(typeof(decimal), "0.01", "1000")] public decimal PricePerKm { get; set; }
        [Range(typeof(decimal), "0.01", "1000")] public decimal PricePerMinute { get; set; }
        [Range(typeof(decimal), "0.01", "1000")] public decimal MinimumFare { get; set; }
        public bool IsActive { get; set; }
    }
}
