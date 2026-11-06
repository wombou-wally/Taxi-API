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
        private readonly TaxiDbContext _context;
        private readonly IFareService _fareService;
        private readonly IDistanceService _distanceService;
        private const double Speed = 25;
        private const double SearchRadiusKm = 10.0;
        private const int MaxCandidates = 200;
        private const decimal MaxFareMultiplier = 2m;
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
            var vehicleType = await _context.VehicleTypes.FirstOrDefaultAsync(t => t.Id == request.VehicleTypeId && t.IsActive);
            if (vehicleType == null)
            {
                return Problem(statusCode: 400, title: "Vehicle type not found or inactive");
            }
            var distance = _distanceService.DistanceKm(request.PickupLat, request.PickupLog, request.DropoffLat, request.DropoffLog);
            if (distance < 0.1)
            {
                return Problem(statusCode: 400, title: "Distance too short");
            }
            if (distance > 100.0)
            {
                return Problem(statusCode: 400, title: "Distance too far");
            }
            var hasActive = await _context.Rides.AnyAsync(r => r.CustomerId == customerId && (r.Status == RideStatus.Requested || r.Status == RideStatus.Accepted || r.Status == RideStatus.InProgress));
            if (hasActive)
            {
                return Problem(statusCode: 409, title: "You already have an active ride");
            }
            double minutes = distance / Speed * 60;
            minutes = Math.Max(1, Math.Ceiling(minutes));
            var fare = _fareService.CalculateFare(vehicleType, distance, minutes);

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
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {

                return Problem(statusCode: 409, title: "You already have an active ride");
            }
            var response = ToResponse(ride, vehicleType.Name);

            return CreatedAtAction(nameof(GetRide), new { id = ride.Id }, response);
        }
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IEnumerable<RideResponse>>> GetMyRides(int limit = 20)
        {
            int id = User.GetUserId();
            Role role = User.GetUserRole();
            if (limit < 1 || limit > 50)
            {
                return Problem(statusCode: 400, title: "Limit must be between 1 and 50");
            }
            var rides = await _context.Rides.AsNoTracking()
                .VisibleTo(id, role)
                .Include(r => r.VehicleType)
                .OrderByDescending(r => r.RequestedAt)
                .ThenByDescending(r => r.Id)
                .Take(limit)
                .ToListAsync();
            var response = rides.Select(r => ToResponse(r, r.VehicleType.Name)).ToList();
            return Ok(response);
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
                    .ExecuteUpdateAsync(s => s
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

            var ride = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == id);
            return Ok(ToResponse(ride, ride.VehicleType.Name));
        }
        [HttpPost("{id:int}/start")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<RideResponse>> StartRide(int id)
        {
            int change;
            int driverId = User.GetUserId();
            change = await _context.Rides.Where(r => r.Id == id && r.DriverProfileUserId == driverId && r.Status == RideStatus.Accepted)
               .ExecuteUpdateAsync(s => s
                   .SetProperty(r => r.Status, RideStatus.InProgress)
                   .SetProperty(r => r.StartedAt, DateTime.UtcNow)
               );
            if (change == 0)
            {
                var exists = await _context.Rides.AsNoTracking().AnyAsync(r => r.Id == id && r.DriverProfileUserId == driverId);

                if (!exists)
                {
                    return Problem(statusCode: 404, title: "Ride not found");
                }
                return Problem(statusCode: 409, title: "Ride cannot be started");
            }

            var ride = await _context.Rides.Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == id);
            return Ok(ToResponse(ride, ride.VehicleType.Name));

        }
        [HttpPost("{id:int}/complete")]
        [Authorize(Roles = nameof(Role.Driver))]
        public async Task<ActionResult<RideResponse>> Complete(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelRideRequest? request)
        {
            int driverId = User.GetUserId();
            var exist = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstOrDefaultAsync(r => r.Id == id && r.DriverProfileUserId == driverId);
            if (exist == null)
            {
                return Problem(statusCode: 404, title: "ride not found");
            }
            if (exist.Status != RideStatus.InProgress)
            {
                return Problem(statusCode: 409, title: "ride can't be completed");
            }

            var started = exist.StartedAt ?? DateTime.UtcNow;
            var minutes = (DateTime.UtcNow - started).TotalMinutes;
            minutes = Math.Max(1, Math.Ceiling(minutes));
            var distance = _distanceService.DistanceKm(exist.PickupLatitude, exist.PickupLongitude, exist.DropoffLatitude, exist.DropoffLongitude);
            var fare = _fareService.CalculateFare(exist.VehicleType, distance, minutes);
            fare = Math.Min(fare, exist.EstimatedFare * MaxFareMultiplier);

            var changed = await _context.Rides.Where(r => r.Id == id && r.DriverProfileUserId == driverId && r.Status == RideStatus.InProgress)
                .ExecuteUpdateAsync(s => s
                .SetProperty(s => s.Status, RideStatus.Completed)
                .SetProperty(s => s.CompletedAt, DateTime.UtcNow)
                .SetProperty(s => s.FinalFare, fare)
                );
            if (changed == 0)
            {
                return Problem(statusCode: 409, title: "ride can't be completed");
            }
            var ride = await _context.Rides.Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == id);
            return Ok(ToResponse(ride, ride.VehicleType.Name));
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
                .ExecuteUpdateAsync(s => s
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

            var ride = await _context.Rides.AsNoTracking()
                .Include(r => r.VehicleType)
                .FirstAsync(r => r.Id == id);
            return Ok(ToResponse(ride, ride.VehicleType.Name));
        }

        private static bool IsUniqueViolation(Exception ex)
        {
            return ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
        }
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
                return (null, Problem(statusCode: 409, title: "Go online to see rides"));
            }
            if (driver.CurrentLatitude == null || driver.CurrentLongitude == null)
            {
                return (null, Problem(statusCode: 409, title: "Share your location to see rides"));
            }
            if (driver.ActiveVehicle == null)
            {
                return (null, Problem(statusCode: 409, title: "Select an active vehicle to see rides"));
            }
            return (driver, null);
        }
        private static RideResponse ToResponse(Ride ride, string vehicleTypeName)
        {
            return new RideResponse
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
}
