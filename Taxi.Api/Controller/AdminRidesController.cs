using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;
using Taxi.Api.DTO.Ride;
using Taxi.Api.Models;
using Taxi.Api.Services;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/admin/rides")]
    [Authorize(Roles = nameof(Role.Admin))]
    public class AdminRidesController : ControllerBase
    {
        private const int MaxPageSize = 100;
        private const int MaxPage = 100000;

        private readonly TaxiDbContext _context;
        private readonly IRideNotifier _notifier;

        public AdminRidesController(TaxiDbContext context, IRideNotifier notifier)
        {
            _context = context;
            _notifier = notifier;
        }

        /// <summary>
        /// Lists rides with optional filters (admin only).
        /// </summary>
        /// <remarks>
        /// Filter by status, customer, driver and a requested-at date range. Newest first, paged (page size 1-100).
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResponse<AdminRideResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<PagedResponse<AdminRideResponse>>> ListRides(
            RideStatus? status,
            int? customerId,
            int? driverId,
            DateTime? from,
            DateTime? to,
            int page = 1,
            int pageSize = 20)
        {
            if (page < 1 || page > MaxPage)
            {
                return Problem(statusCode: 400, title: "Page is out of range");
            }
            if (pageSize < 1 || pageSize > MaxPageSize)
            {
                return Problem(statusCode: 400, title: "Page size must be between 1 and 100");
            }
            if (from.HasValue && to.HasValue && from.Value > to.Value)
            {
                return Problem(statusCode: 400, title: "'from' must not be after 'to'");
            }

            IQueryable<Ride> query = _context.Rides.AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(r => r.Status == status.Value);
            }
            if (customerId.HasValue)
            {
                query = query.Where(r => r.CustomerId == customerId.Value);
            }
            if (driverId.HasValue)
            {
                query = query.Where(r => r.DriverProfileUserId == driverId.Value);
            }
            if (from.HasValue)
            {
                var fromUtc = from.Value.ToUniversalTime();
                query = query.Where(r => r.RequestedAt >= fromUtc);
            }
            if (to.HasValue)
            {
                var toUtc = to.Value.ToUniversalTime();
                query = query.Where(r => r.RequestedAt <= toUtc);
            }

            int total = await query.CountAsync();

            var rides = await query
                .Include(r => r.VehicleType)
                .OrderByDescending(r => r.RequestedAt)
                .ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = new PagedResponse<AdminRideResponse>
            {
                Items = rides.Select(ToAdminResponse).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };
            return Ok(response);
        }

        /// <summary>
        /// Cancels a ride that is still active, whatever its stage (admin only).
        /// </summary>
        /// <remarks>
        /// A reason is required. Returns 409 if the ride is already completed or cancelled.
        /// </remarks>
        [HttpPost("{id:int}/cancel")]
        [ProducesResponseType(typeof(AdminRideResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<AdminRideResponse>> ForceCancelRide(int id, AdminCancelRequest request)
        {
            var reason = request.Reason.Trim();

            int changed = await _context.Rides
                .Where(r => r.Id == id
                            && (r.Status == RideStatus.Requested
                                || r.Status == RideStatus.Accepted
                                || r.Status == RideStatus.InProgress))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RideStatus.Cancelled)
                    .SetProperty(r => r.CancelledAt, DateTime.UtcNow)
                    .SetProperty(r => r.CancelledBy, Role.Admin)
                    .SetProperty(r => r.CancelReason, reason));

            if (changed == 0)
            {
                var exists = await _context.Rides.AsNoTracking().AnyAsync(r => r.Id == id);
                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride cannot be cancelled");
            }

            var ride = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == id);

            await _notifier.NotifyRideChangedAsync(id, ride.Status.ToString(), ride.CustomerId, ride.DriverProfileUserId);
            if (ride.DriverProfileUserId == null)
            {
                await _notifier.NotifyAvailableRidesChangedAsync();
            }

            return Ok(ToAdminResponse(ride));
        }

        private static AdminRideResponse ToAdminResponse(Ride ride)
        {
            return new AdminRideResponse
            {
                Id = ride.Id,
                CustomerId = ride.CustomerId,
                DriverId = ride.DriverProfileUserId,
                VehicleId = ride.VehicleId,
                VehicleTypeId = ride.VehicleTypeId,
                VehicleTypeName = ride.VehicleType.Name,
                Status = ride.Status.ToString(),
                PickupLat = ride.PickupLatitude,
                PickupLog = ride.PickupLongitude,
                DropoffLat = ride.DropoffLatitude,
                DropoffLog = ride.DropoffLongitude,
                EstimatedFare = ride.EstimatedFare,
                FinalFare = ride.FinalFare,
                RequestedAt = ride.RequestedAt,
                AcceptedAt = ride.AcceptedAt,
                StartedAt = ride.StartedAt,
                CompletedAt = ride.CompletedAt,
                CancelledAt = ride.CancelledAt,
                CancelledBy = ride.CancelledBy?.ToString(),
                CancelReason = ride.CancelReason
            };
        }
    }
}