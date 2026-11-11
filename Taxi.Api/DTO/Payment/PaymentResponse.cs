namespace Taxi.Api.DTO.Payment
{
    public class PaymentResponse
    {
        public int Id { get; set; }
        public int RideId { get; set; }
        public decimal Amount { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
    }
}