using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;
using Taxi.Api.DTO.Payment;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/rides/{id:int}/payment")]
    public class PaymentsController : ControllerBase
    {
        private readonly TaxiDbContext _context;

        public PaymentsController(TaxiDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Records the payment for a completed ride (driver only).
        /// </summary>
        /// <remarks>
        /// The amount is taken from the ride's final fare. One payment per ride.
        /// </remarks>
        [HttpPost]
        [Authorize(Roles = nameof(Role.Driver))]
<<<<<<< HEAD
        [Authorize(Roles = nameof(Role.Driver))]
=======
>>>>>>> OpenAPIDoc-1.043
        [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<PaymentResponse>> RecordPayment(int id, RecordPaymentRequest request)
        {
            int driverId = User.GetUserId();

            if (!Enum.IsDefined(request.Method))
            {
                return Problem(statusCode: 400, title: "Invalid payment method");
            }

            var ride = await _context.Rides.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.DriverProfileUserId == driverId);
            if (ride == null)
            {
                return Problem(statusCode: 404, title: "Ride not found");
            }
            if (ride.Status != RideStatus.Completed || ride.FinalFare == null)
            {
                return Problem(statusCode: 409, title: "Ride is not completed");
            }

            var alreadyPaid = await _context.Payments.AnyAsync(p => p.RideId == id);
            if (alreadyPaid)
            {
                return Problem(statusCode: 409, title: "Payment already recorded");
            }

            var payment = new Payment
            {
                RideId = id,
                Amount = ride.FinalFare.Value,
                Method = request.Method,
                Status = PaymentStatus.Paid,
                PaidAt = DateTime.UtcNow,
                RecordedByDriverId = driverId
            };
            _context.Payments.Add(payment);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // Two requests passed the check at the same time; the unique index let only one in.
                return Problem(statusCode: 409, title: "Payment already recorded");
            }

            return CreatedAtAction(nameof(GetPayment), new { id }, ToResponse(payment));
        }

        /// <summary>
        /// Returns the payment of a ride the logged-in user is allowed to see.
        /// </summary>
        [HttpGet]
        [Authorize]
        [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PaymentResponse>> GetPayment(int id)
        {
            int userId = User.GetUserId();
            Role role = User.GetUserRole();

            var visible = await _context.Rides.AsNoTracking()
                .VisibleTo(userId, role)
                .AnyAsync(r => r.Id == id);
            if (!visible)
            {
                return Problem(statusCode: 404, title: "Ride not found");
            }

            var payment = await _context.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.RideId == id);
            if (payment == null)
            {
                return Problem(statusCode: 404, title: "Payment not found");
            }

            return Ok(ToResponse(payment));
        }

        private static PaymentResponse ToResponse(Payment payment) => new()
        {
            Id = payment.Id,
            RideId = payment.RideId,
            Amount = payment.Amount,
            Method = payment.Method.ToString(),
            Status = payment.Status.ToString(),
            PaidAt = payment.PaidAt
        };
    }
}