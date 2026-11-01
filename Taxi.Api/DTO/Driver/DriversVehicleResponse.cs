namespace Taxi.Api.DTO.Driver
{
    public class DriversVehicleResponse
    {
        public int Id { get; set; }
        public string Model { get; set; } = string.Empty;
        public string PlateNumber { get; set; } = string.Empty;
        public string InsuranceNumber { get; set; } = string.Empty;
        public  DateOnly InsuranceExpiryDate { get; set; }
        public int VehicleTypeId { get; set; }
    }
}
