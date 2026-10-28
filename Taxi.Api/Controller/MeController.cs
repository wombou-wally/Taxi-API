using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taxi.Api.Data;
using Taxi.Api.Users;
using Taxi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/me")]
    [Authorize]
    public class MeController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        public MeController(TaxiDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<ProfileResponse>> Get()
        {
            var userId = User.GetUserId();
            var profile = await _context.Users.Where(u => u.Id == userId)
                .Select(p => new ProfileResponse
                {
                    Id = p.Id,
                    FullName = p.FullName,
                    Phone = p.Phone,
                    Role = p.Role,
                    Email = p.Email,
                    CreatedAt = p.CreatedAt
                }).FirstOrDefaultAsync();

            if (profile == null)
            {
                return Problem(statusCode: 404, title: "Profile not found");
            }

            return Ok(profile);
        }
        [HttpPut]
        public async Task<ActionResult<ProfileResponse>> Update(UpdateProfileRequest request)
        {
            var userId = User.GetUserId();

            var existing = await _context.Users.FindAsync(userId);
            if (existing == null)
            {
                return Problem(statusCode: 404, title: "Profile not found");
            }
            existing.FullName = request.FullName.Trim();
            existing.Phone = request.Phone.Trim();

            await _context.SaveChangesAsync();
            var profile = new ProfileResponse
            {
                Id = existing.Id,
                FullName = existing.FullName,
                Phone = existing.Phone,
                Email = existing.Email,
                CreatedAt = existing.CreatedAt,
                Role = existing.Role,
            };
            return Ok(profile);
        }
    }
}
