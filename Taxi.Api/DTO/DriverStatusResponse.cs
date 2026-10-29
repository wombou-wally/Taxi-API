using Taxi.Api.Models;

namespace Taxi.Api.DTO
{
    public class DriverStatusResponse
    {
        public int UserId { get; set; }
        public ApprovalStatus Status { get; set; }
    }
}
