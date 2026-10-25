using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.Models;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]

    public class AuthController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        private readonly IPasswordHasher<User> _hasher;

        public AuthController(TaxiDbContext context, IPasswordHasher<User> hasher)
        {
            _context = context;
            _hasher = hasher;
        }

        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponce>> Register(RegisterRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            bool emailExists =  await _context.Users.AnyAsync(e => e.Email == email);

            if(emailExists)
            {
                return Problem(statusCode: 409, title: "Email Already Associated to an Account");
            }
            var user = new User
            {
                Email = email,
                PasswordHash = "",
                FullName = request.FullName,
                Role = Role.Customer,
                Phone = request.Phone,
            };

            user.PasswordHash = _hasher.HashPassword(user, request.Password);
             _context.Add(user);
            await _context.SaveChangesAsync();

            RegisterResponce res = new RegisterResponce
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = user.Role,

            };
            return StatusCode(StatusCodes.Status201Created , res);
        }
    }
}
