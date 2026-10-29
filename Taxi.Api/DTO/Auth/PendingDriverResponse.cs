namespace Taxi.Api.DTO.Auth
{
    public class PendingDriverResponse
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string LicenceNumber { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
    }
}
