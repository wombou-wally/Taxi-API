using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Vehicles
{
    public class VehicleResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal BaseFare { get; set; }
        public decimal PricePerKm { get; set; }
        public decimal PricePerMinute { get; set; }
        public decimal MinimumFare { get; set; }
        public bool IsActive { get; set; }
    }
}
