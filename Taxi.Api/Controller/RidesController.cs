using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            if(ride == null)
            {
               return Problem(statusCode: 404, title: "Ride not found");
            }
            return Ok(ToResponse(ride, ride.VehicleType.Name));
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
                RequestedAt = ride.RequestedAt,
                Status = ride.Status.ToString(),
                VehicleTypeId = ride.VehicleTypeId,

            };
        }
    }
}
