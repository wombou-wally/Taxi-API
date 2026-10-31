namespace Taxi.Api.Models
{
    public class DriverProfile
    {
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
        public bool IsOnline { get; set; } = false;
        public double? CurrentLatitude { get; set; }
        public double? CurrentLongitude { get; set; }
        public DateTime? LocationUpdatedAt { get; set; }
        public string LicenceNumber { get; set; } = string.Empty;

        public List<Vehicle> Vehicles { get; set; } = new();
    }
}