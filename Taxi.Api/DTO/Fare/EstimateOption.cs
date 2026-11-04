namespace Taxi.Api.DTO.Fare
{
    public class EstimateOption
    {
        public int VehicleTypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Seats { get; set; }
        public decimal EstimatedFare { get; set; }
    }
}
