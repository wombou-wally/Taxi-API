using Taxi.Api.Models;

namespace Taxi.Api.DTO.Driver
{
    public class DriverStatusResponse
    {
        public int UserId { get; set; }
        public ApprovalStatus Status { get; set; }
    }
}
