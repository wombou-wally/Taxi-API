namespace Taxi.Api.Models;

public class VehicleType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Seats { get; set; }
    public decimal BaseFare { get; set; }
    public decimal PricePerKm { get; set; }
    public decimal PricePerMinute { get; set; }
    public decimal MinimumFare { get; set; }
    public bool IsActive { get; set; } = true;
}