using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Collections;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.DTO.Driver;
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

        [HttpPost("register")]
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
                //PasswordHash = "",
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

        [Authorize(Roles =nameof(Role.Admin))]
        [HttpGet("pending")]
        public async Task<ActionResult<IEnumerable<PendingDriverResponse>>> PendingDrivers()
        {
            var pendingDrivers = await _context.DriverProfiles.Where(d => d.Status == ApprovalStatus.Pending).OrderBy(d => d.User.CreatedAt).Select(d => 
            new PendingDriverResponse
            {
                UserId = d.UserId,
                Email = d.User.Email,
                FullName = d.User.FullName,
                LicenceNumber = d.LicenceNumber,
                Phone = d.User.Phone,
                RegisteredAt = d.User.CreatedAt
            }
            ).ToListAsync();
               return Ok(pendingDrivers);
        }

        [Authorize (Roles = nameof(Role.Admin))]
        [HttpPost("approve/{userId:int}")]
        public async Task<ActionResult<DriverStatusResponse>> Approve(int userId)
        {
            var driver = await _context.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userId);
            if (driver == null)
            {
                return Problem(statusCode: 404, title: "No Driver with enterd Id");
            }
            else if(driver.Status != ApprovalStatus.Pending)
            {
                return Problem(statusCode: 409, title: "Driver status is not pending");
            }
            driver.Status = ApprovalStatus.Approved;
            await _context.SaveChangesAsync();
            var response = new DriverStatusResponse
            {
                UserId = userId,
                Status = driver.Status,
            };
            return Ok(response);
        }

        [Authorize(Roles = nameof(Role.Admin))]
        [HttpPost("reject/{userId:int}")]
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
            var response = new DriverStatusResponse
            {
                UserId = userId,
                Status = driver.Status,
            };
            return Ok(response);
        }
    }
}
