using System.ComponentModel.DataAnnotations;

namespace Taxi.Api.Users
{
    public class UpdateProfileRequest
    {
        [Required][MaxLength(100)]public required string FullName { get; set; }
        [Required][MaxLength(30)]public required string Phone { get; set; }
    }
}
