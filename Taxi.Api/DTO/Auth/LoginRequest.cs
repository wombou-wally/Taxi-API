using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Auth
{
    public class LoginRequest
    {
        [EmailAddress][Required][MaxLength(256)] public required string Email { get; set; }
        [MaxLength(100)]public required string Password { get; set; }
    }
}
