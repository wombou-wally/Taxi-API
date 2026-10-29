using Taxi.Api.Models;

namespace Taxi.Api.DTO.Auth
{
    public class RegisterDriverResponse
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public ApprovalStatus Status { get; set; }
    }
}
