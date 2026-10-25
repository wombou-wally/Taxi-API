using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.DTO.Auth
{
    public class RegisterRequest
    {
        [Required][MaxLength(256)] [EmailAddress] public required string Email { get; set; }
        [Required][MinLength(8)][MaxLength(100)] public required string Password { get; set; }
        [Required] [MaxLength(100)] public required string FullName { get; set; }
        [Required][MaxLength(30)] public required string Phone { get; set; }
    }

}
