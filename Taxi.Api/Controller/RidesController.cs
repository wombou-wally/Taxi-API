using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taxi.Api.Data;
using Taxi.Api.DTO.Ride;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;
using Taxi.Api.Services;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/rides")]
    public class RidesController : ControllerBase
    {
        private const double SearchRadiusKm = 10.0;
        private const int MaxCandidates = 200;

        private readonly TaxiDbContext _context;
        private readonly IFareService _fareService;
        private readonly IDistanceService _distanceService;

        public RidesController(TaxiDbContext context, IFareService fareService, IDistanceService distanceService)
        {
            _context = context;
            _fareService = fareService;
            _distanceService = distanceService;
        }

        [HttpPost]
        [Authorize(Roles = nameof(Role.Customer))]
        public async Task<ActionResult<RideResponse>> RequestRide(RideRequest request)
        {
            int customerId = User.GetUserId();

            var vehicleType = await _context.VehicleTypes
                .FirstOrDefaultAsync(t => t.Id == request.VehicleTypeId && t.IsActive);
            if (vehicleType == null)
            {
                return Problem(statusCode: 400, title: "Vehicle type not found or inactive");
            }

            var distance = _distanceService.DistanceKm(request.PickupLat, request.PickupLog, request.DropoffLat, request.DropoffLog);
            if (distance < RideRules.MinDistanceKm)
            {
                return Problem(statusCode: 400, title: "Distance too short");
            }
            if (distance > RideRules.MaxDistanceKm)
            {
                return Problem(statusCode: 400, title: "Distance too far");
            }

            var hasActive = await _context.Rides.AnyAsync(r => r.CustomerId == customerId
                && (r.Status == RideStatus.Requested || r.Status == RideStatus.Accepted || r.Status == RideStatus.InProgress));
            if (hasActive)
            {
                return Problem(statusCode: 409, title: "You already have an active ride");
            }

            var fare = _fareService.CalculateFare(vehicleType, distance, RideRules.EstimatedMinutes(distance));

            var ride = new Ride
            {
                CustomerId = customerId,
                VehicleTypeId = vehicleType.Id,
                PickupLatitude = request.PickupLat,
                PickupLongitude = request.PickupLog,
                DropoffLatitude = request.DropoffLat,
                DropoffLongitude = request.DropoffLog,
                EstimatedFare = fare,
            };
            _context.Rides.Add(ride);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Problem(statusCode: 409, title: "You already have an active ride");
            }

            return CreatedAtAction(nameof(GetRide), new { id = ride.Id }, ToResponse(ride, vehicleType.Name));
        }

        [HttpGet("mine")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<RideResponse>>> GetMyRides(int limit = 20)
        {
            int userId = User.GetUserId();
            Role role = User.GetUserRole();
            if (limit < 1 || limit > 50)
            {
                return Problem(statusCode: 400, title: "Limit must be between 1 and 50");
            }

            var rides = await _context.Rides.AsNoTracking()
                .VisibleTo(userId, role)
                .Include(r => r.VehicleType)
                .OrderByDescending(r => r.RequestedAt)
                .ThenByDescending(r => r.Id)
                .Take(limit)
                .ToListAsync();

            return Ok(rides.Select(r => ToResponse(r, r.VehicleType.Name)).ToList());
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<ActionResult<RideResponse>> GetRide(int id)
        {
            int userId = User.GetUserId();
            Role role = User.GetUserRole();

            var ride = await _context.Rides.AsNoTracking()
                .VisibleTo(userId, role)
                .Include(r => r.VehicleType)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (ride == null)
            {
                return Problem(statusCode: 404, title: "Ride not found");
            }

            return Ok(ToResponse(ride, ride.VehicleType.Name));
        }

        [HttpGet("available")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<IEnumerable<AvailableRideResponse>>> GetAvailableRides(int limit = 20)
        {
            int driverId = User.GetUserId();
            if (limit < 1 || limit > 50)
            {
                return Problem(statusCode: 400, title: "Limit must be between 1 and 50");
            }

            var (driver, error) = await LoadReadyDriverAsync(driverId);
            if (error != null)
            {
                return error;
            }
            double driverLat = driver!.CurrentLatitude!.Value;
            double driverLon = driver.CurrentLongitude!.Value;
            int vehicleTypeId = driver.ActiveVehicle!.VehicleTypeId;

            var candidates = await _context.Rides.AsNoTracking()
                .Where(r => r.Status == RideStatus.Requested
                            && r.DriverProfileUserId == null
                            && r.VehicleTypeId == vehicleTypeId)
                .Include(r => r.VehicleType)
                .OrderByDescending(r => r.RequestedAt)
                .Take(MaxCandidates)
                .ToListAsync();

            var response = candidates
                .Select(r => new
                {
                    Ride = r,
                    Distance = _distanceService.DistanceKm(driverLat, driverLon, r.PickupLatitude, r.PickupLongitude)
                })
                .Where(x => x.Distance <= SearchRadiusKm)
                .OrderBy(x => x.Distance)
                .Take(limit)
                .Select(x => new AvailableRideResponse
                {
                    Id = x.Ride.Id,
                    VehicleTypeName = x.Ride.VehicleType.Name,
                    PickupLat = x.Ride.PickupLatitude,
                    PickupLog = x.Ride.PickupLongitude,
                    DropoffLat = x.Ride.DropoffLatitude,
                    DropoffLog = x.Ride.DropoffLongitude,
                    EstimatedFare = x.Ride.EstimatedFare,
                    DistanceToPickupKm = Math.Round(x.Distance, 1),
                    RequestedAt = x.Ride.RequestedAt
                })
                .ToList();

            return Ok(response);
        }

        [HttpPost("{id:int}/accept")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<RideResponse>> AcceptRide(int id)
        {
            int driverId = User.GetUserId();

            var (driver, error) = await LoadReadyDriverAsync(driverId);
            if (error != null)
            {
                return error;
            }
            int vehicleTypeId = driver!.ActiveVehicle!.VehicleTypeId;
            int vehicleId = driver.ActiveVehicle.Id;

            var busy = await _context.Rides.AnyAsync(r =>
                r.DriverProfileUserId == driverId
                && (r.Status == RideStatus.Accepted || r.Status == RideStatus.InProgress));
            if (busy)
            {
                return Problem(statusCode: 409, title: "You already have an active ride");
            }

            int changed;
            try
            {
                changed = await _context.Rides
                    .Where(r => r.Id == id
                                && r.Status == RideStatus.Requested
                                && r.DriverProfileUserId == null
                                && r.VehicleTypeId == vehicleTypeId)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(r => r.Status, RideStatus.Accepted)
                        .SetProperty(r => r.DriverProfileUserId, driverId)
                        .SetProperty(r => r.VehicleId, vehicleId)
                        .SetProperty(r => r.AcceptedAt, DateTime.UtcNow));
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                return Problem(statusCode: 409, title: "You already have an active ride");
            }

            if (changed == 0)
            {
                var exists = await _context.Rides.AsNoTracking().AnyAsync(r => r.Id == id);
                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride is no longer available");
            }

            return Ok(await ReloadResponseAsync(id));
        }

        [HttpPost("{id:int}/start")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<RideResponse>> StartRide(int id)
        {
            int driverId = User.GetUserId();

            int changed = await _context.Rides
                .Where(r => r.Id == id && r.DriverProfileUserId == driverId && r.Status == RideStatus.Accepted)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, RideStatus.InProgress)
                    .SetProperty(r => r.StartedAt, DateTime.UtcNow));

            if (changed == 0)
            {
                var exists = await _context.Rides.AsNoTracking()
                    .AnyAsync(r => r.Id == id && r.DriverProfileUserId == driverId);
                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride cannot be started");
            }

            return Ok(await ReloadResponseAsync(id));
        }

        [HttpPost("{id:int}/complete")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<RideResponse>> CompleteRide(int id)
        {
            int driverId = User.GetUserId();

            var ride = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstOrDefaultAsync(r => r.Id == id && r.DriverProfileUserId == driverId);
            if (ride == null)
            {
                return Problem(statusCode: 404, title: "Ride not found");
            }
            if (ride.Status != RideStatus.InProgress)
            {
                return Problem(statusCode: 409, title: "Ride cannot be completed");
            }

            var now = DateTime.UtcNow;
            var minutes = RideRules.ElapsedMinutes(ride.StartedAt ?? now, now);
            var distance = _distanceService.DistanceKm(ride.PickupLatitude, ride.PickupLongitude, ride.DropoffLatitude, ride.DropoffLongitude);
            var fare = _fareService.CalculateFare(ride.VehicleType, distance, minutes);
            fare = Math.Min(fare, ride.EstimatedFare * RideRules.MaxFareMultiplier);

            int changed = await _context.Rides
                .Where(r => r.Id == id && r.DriverProfileUserId == driverId && r.Status == RideStatus.InProgress)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, RideStatus.Completed)
                    .SetProperty(r => r.CompletedAt, now)
                    .SetProperty(r => r.FinalFare, fare));

            if (changed == 0)
            {
                return Problem(statusCode: 409, title: "Ride cannot be completed");
            }

            return Ok(await ReloadResponseAsync(id));
        }

        [HttpPost("{id:int}/cancel")]
        [Authorize(Roles = nameof(Role.Customer))]
        public async Task<ActionResult<RideResponse>> CancelRide(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelRideRequest? request)
        {
            int customerId = User.GetUserId();

            var reason = request?.Reason?.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                reason = null;
            }

            int changed = await _context.Rides
                .Where(r => r.Id == id
                            && r.CustomerId == customerId
                            && (r.Status == RideStatus.Requested || r.Status == RideStatus.Accepted))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, RideStatus.Cancelled)
                    .SetProperty(r => r.CancelledAt, DateTime.UtcNow)
                    .SetProperty(r => r.CancelledBy, Role.Customer)
                    .SetProperty(r => r.CancelReason, reason));

            if (changed == 0)
            {
                var exists = await _context.Rides.AsNoTracking()
                    .AnyAsync(r => r.Id == id && r.CustomerId == customerId);
                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride cannot be cancelled");
            }

            return Ok(await ReloadResponseAsync(id));
        }

        [HttpPost("{id:int}/driver-cancel")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<IActionResult> DriverCancelRide(int id)
        {
            int driverId = User.GetUserId();

            int changed = await _context.Rides
                .Where(r => r.Id == id && r.DriverProfileUserId == driverId && r.Status == RideStatus.Accepted)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, RideStatus.Requested)
                    .SetProperty(r => r.DriverProfileUserId, (int?)null)
                    .SetProperty(r => r.VehicleId, (int?)null)
                    .SetProperty(r => r.AcceptedAt, (DateTime?)null));

            if (changed == 0)
            {
                var exists = await _context.Rides.AsNoTracking()
                    .AnyAsync(r => r.Id == id && r.DriverProfileUserId == driverId);
                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride cannot be cancelled");
            }

            return NoContent();
        }

        // ---------- helpers ----------

        // ExecuteUpdate does not touch tracked objects, so the response is read fresh.
        private async Task<RideResponse> ReloadResponseAsync(int rideId)
        {
            var ride = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == rideId);
            return ToResponse(ride, ride.VehicleType.Name);
        }

        private static bool IsUniqueViolation(Exception ex) =>
            ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private async Task<(DriverProfile? Driver, ActionResult? Error)> LoadReadyDriverAsync(int driverId)
        {
            var driver = await _context.DriverProfiles.AsNoTracking()
                .Include(d => d.ActiveVehicle)
                .FirstOrDefaultAsync(d => d.UserId == driverId);
            if (driver == null)
            {
                return (null, Problem(statusCode: 404, title: "Driver profile not found"));
            }
            if (driver.Status != ApprovalStatus.Approved)
            {
                return (null, Problem(statusCode: 403, title: "Your driver account is not approved"));
            }
            if (!driver.IsOnline)
            {
                return (null, Problem(statusCode: 409, title: "Go online first"));
            }
            if (driver.CurrentLatitude == null || driver.CurrentLongitude == null)
            {
                return (null, Problem(statusCode: 409, title: "Share your location first"));
            }
            if (driver.ActiveVehicle == null)
            {
                return (null, Problem(statusCode: 409, title: "Select an active vehicle first"));
            }
            return (driver, null);
        }

        private static RideResponse ToResponse(Ride ride, string vehicleTypeName) => new()
        {
            Id = ride.Id,
            Name = vehicleTypeName,
            PickupLat = ride.PickupLatitude,
            PickupLog = ride.PickupLongitude,
            DropoffLat = ride.DropoffLatitude,
            DropoffLog = ride.DropoffLongitude,
            EstimatedFare = ride.EstimatedFare,
            FinalFare = ride.FinalFare,
            RequestedAt = ride.RequestedAt,
            CompletedAt = ride.CompletedAt,
            CancelledAt = ride.CancelledAt,
            CancelledBy = ride.CancelledBy?.ToString(),
            CancelReason = ride.CancelReason,
            Status = ride.Status.ToString(),
            VehicleTypeId = ride.VehicleTypeId,
        };
    }
}