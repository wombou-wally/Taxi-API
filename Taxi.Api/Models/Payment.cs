namespace Taxi.Api.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int RideId { get; set; }
        public Ride Ride { get; set; } = null!;
        public decimal Amount { get; set; }
        public PaymentMethod Method { get; set; }
        public PaymentStatus Status { get; set; }
        public DateTime? PaidAt { get; set; }
        public int RecordedByDriverId { get; set; }
        public DriverProfile RecordedByDriver { get; set; } = null!;
    }
}
