namespace Taxi.Api.DTO.Auth
{
    public class LoginResponse
    {
        public required string Token { get; set; }
        public DateTime ExpiresAt { get; set; } 
    }
}
