namespace Taxi.Api.DTO.Fare
{
    public class EstimateResponse
    {
        public double StraightLineKm { get; set; }
        public int EstimatedMinutes { get; set; }
        public List<EstimateOption> Options { get; set; } = new List<EstimateOption>();
    }
}
