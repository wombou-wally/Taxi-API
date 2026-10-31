namespace Taxi.Api.Models
{
    public class Vehicle
    {
        public int Id { get; set; }
        public string Model { get; set; } = string.Empty;
        public string PlateNumber { get; set; } = string.Empty;
        public string InsuranceNumber { get; set; } = string.Empty;
        public DateOnly InsuranceExpiryDate { get; set; }

        public int DriverProfileUserId { get; set; }
        public DriverProfile Driver { get; set; } = null!;

        public int VehicleTypeId { get; set; }
        public VehicleType VehicleType { get; set; } = null!;
    }
}