using Taxi.Api.Models;

namespace Taxi.Api.DTO.Auth
{
    public class RegisterResponce
    {
        public required int Id { get; set; }
        public required string Email { get; set; }
        public required string FullName { get; set; }
        public required string Phone { get; set; }
        public Role Role = Role.Customer;
    }
}
