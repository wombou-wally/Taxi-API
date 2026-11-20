using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.DTO.Driver;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/drivers")]
    public class DriversController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        private readonly IPasswordHasher<User> _hasher;

        public DriversController(TaxiDbContext context, IPasswordHasher<User> hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        /// <summary>
        /// Registers a new driver account.
        /// </summary>
        /// <remarks>
        /// The driver starts as Pending and cannot go online until an admin approves the account.
        /// </remarks>
        [HttpPost("register")]
        [ProducesResponseType(typeof(RegisterDriverResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RegisterDriverResponse>> Register(RegisterDriverRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            bool emailExists = await _context.Users.AnyAsync(e => e.Email == email);

            if (emailExists)
            {
                return Problem(statusCode: 409, title: "Email Already Associated to an Account");
            }
            var driver = new DriverProfile
            {
                LicenceNumber = request.LicenceNumber.Trim()
            };
            var user = new User
            {
                Email = email,
                FullName = request.FullName.Trim(),
                Role = Role.Driver,
                Phone = request.Phone.Trim(),
                DriverProfile = driver
            };
            user.PasswordHash = _hasher.HashPassword(user, request.Password);
            _context.Add(user);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Problem(statusCode: 409, title: "Email Already Associated to an Account");
            }

            RegisterDriverResponse res = new RegisterDriverResponse
            {
                Id = user.Id,
                Email = user.Email,
                Status = driver.Status,
            };
            return StatusCode(StatusCodes.Status201Created, res);
        }

        /// <summary>
        /// Lists drivers waiting for approval (admin only).
        /// </summary>
        /// <remarks>
        /// Oldest registrations first.
        /// </remarks>
        [Authorize(Roles = nameof(Role.Admin))]
        [HttpGet("pending")]
        [ProducesResponseType(typeof(IEnumerable<PendingDriverResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IEnumerable<PendingDriverResponse>>> PendingDrivers()
        {
            var pendingDrivers = await _context.DriverProfiles
                .Where(d => d.Status == ApprovalStatus.Pending)
                .OrderBy(d => d.User.CreatedAt)
                .Select(d => new PendingDriverResponse
                {
                    UserId = d.UserId,
                    Email = d.User.Email,
                    FullName = d.User.FullName,
                    LicenceNumber = d.LicenceNumber,
                    Phone = d.User.Phone,
                    RegisteredAt = d.User.CreatedAt
                })
                .ToListAsync();
            return Ok(pendingDrivers);
        }

        /// <summary>
        /// Approves a pending driver (admin only).
        /// </summary>
        [Authorize(Roles = nameof(Role.Admin))]
        [HttpPost("approve/{userId:int}")]
        [ProducesResponseType(typeof(DriverStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<DriverStatusResponse>> Approve(int userId)
        {
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userId);
            if (driver == null)
            {
                return Problem(statusCode: 404, title: "No Driver with enterd Id");
            }
            else if (driver.Status != ApprovalStatus.Pending)
            {
                return Problem(statusCode: 409, title: "Driver status is not pending");
            }
            driver.Status = ApprovalStatus.Approved;
            await _context.SaveChangesAsync();
            return Ok(new DriverStatusResponse
            {
                UserId = userId,
                Status = driver.Status,
            });
        }

        /// <summary>
        /// Rejects a pending driver (admin only).
        /// </summary>
        [Authorize(Roles = nameof(Role.Admin))]
        [HttpPost("reject/{userId:int}")]
        [ProducesResponseType(typeof(DriverStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<DriverStatusResponse>> Reject(int userId)
        {
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userId);
            if (driver == null)
            {
                return Problem(statusCode: 404, title: "No Driver with enterd Id");
            }
            else if (driver.Status != ApprovalStatus.Pending)
            {
                return Problem(statusCode: 409, title: "Driver status is not pending");
            }
            driver.Status = ApprovalStatus.Rejected;
            await _context.SaveChangesAsync();
            return Ok(new DriverStatusResponse
            {
                UserId = userId,
                Status = driver.Status,
            });
        }

        /// <summary>
        /// Puts the driver online with the chosen vehicle.
        /// </summary>
        /// <remarks>
        /// The driver must be approved, and the vehicle must belong to them and have valid insurance.
        /// </remarks>
        [Authorize(Roles = nameof(Role.Driver))]
        [HttpPost("online")]
        [ProducesResponseType(typeof(DriverAvailabilityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<DriverAvailabilityResponse>> Online(GoOnlineRequest request)
        {
            int id = User.GetUserId();
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(r => r.UserId == id);
            var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.DriverProfileUserId == id);

            if (driver == null)
            {
                return Problem(statusCode: 404, title: "Driver not found");
            }
            if (driver.Status != ApprovalStatus.Approved)
            {
                return Problem(statusCode: 403, title: "Driver has not been approved yet");
            }
            if (vehicle == null)
            {
                return Problem(statusCode: 404, title: "Vehicle not found");
            }
            if (vehicle.InsuranceExpiryDate <= DateOnly.FromDateTime(DateTime.UtcNow))
            {
                return Problem(statusCode: 409, title: "Vehicle Insurance Expiry Date passed");
            }
            driver.ActiveVehicleId = request.VehicleId;
            driver.IsOnline = true;
            await _context.SaveChangesAsync();
            return Ok(new DriverAvailabilityResponse
            {
                IsOnline = driver.IsOnline,
                VehicleId = driver.ActiveVehicleId
            });
        }

        /// <summary>
        /// Takes the driver offline and clears their location.
        /// </summary>
        /// <remarks>
        /// Returns 409 while the driver has an accepted or in-progress ride.
        /// </remarks>
        [Authorize(Roles = nameof(Role.Driver))]
        [HttpPost("offline")]
        [ProducesResponseType(typeof(DriverAvailabilityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<DriverAvailabilityResponse>> Offline()
        {
            int id = User.GetUserId();
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == id);
            if (driver == null)
            {
                return Problem(statusCode: 404, title: "Driver not found");
            }

            var onRide = await _context.Rides.AnyAsync(r =>
                r.DriverProfileUserId == id
                && (r.Status == RideStatus.Accepted || r.Status == RideStatus.InProgress));
            if (onRide)
            {
                return Problem(statusCode: 409, title: "Finish or drop your current ride before going offline");
            }

            driver.IsOnline = false;
            driver.ActiveVehicleId = null;
            driver.CurrentLatitude = null;
            driver.CurrentLongitude = null;
            driver.LocationUpdatedAt = null;
            await _context.SaveChangesAsync();

            return Ok(new DriverAvailabilityResponse
            {
                IsOnline = driver.IsOnline,
                VehicleId = driver.ActiveVehicleId
            });
        }

        /// <summary>
        /// Updates the current location of an online driver.
        /// </summary>
        [Authorize(Roles = nameof(Role.Driver))]
        [HttpPut("location")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult> UpdateLocation(UpdateLocationRequest request)
        {
            int id = User.GetUserId();
            var existing = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == id);

            if (existing == null)
            {
                return Problem(statusCode: 404, title: "Driver not found");
            }
            if (existing.IsOnline == false)
            {
                return Problem(statusCode: 409, title: "Driver not Online");
            }
            existing.CurrentLatitude = request.Latitude;
            existing.CurrentLongitude = request.Longitude;
            existing.LocationUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}