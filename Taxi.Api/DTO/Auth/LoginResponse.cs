namespace Taxi.Api.DTO.Auth
{
    public class LoginResponse
    {
        public required string Token { get; set; }
        public int ExpiryTime { get; set; } 
    }
}
