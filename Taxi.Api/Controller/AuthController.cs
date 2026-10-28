using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Reflection;
using Taxi.Api.Data;
using Taxi.Api.DTO.Auth;
using Taxi.Api.Models;
using Taxi.Api.Services;

namespace Taxi.Api.Controller
{
    [ApiController]
    [Route("api/[controller]")]

    public class AuthController : ControllerBase
    {
        private readonly TaxiDbContext _context;
        private readonly IPasswordHasher<User> _hasher;
        private readonly JwtTokenService _jwtTokenService;

        public AuthController(TaxiDbContext context, IPasswordHasher<User> hasher, JwtTokenService token)
        {
            _context = context;
            _hasher = hasher;
            _jwtTokenService = token;
        }

        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request)
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
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {

                return Problem(statusCode: 409, title: "Email Already Associated to an Account");
            }

            RegisterResponse res = new RegisterResponse
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Phone = user.Phone,
                Role = user.Role,

            };
            return StatusCode(StatusCodes.Status201Created , res);
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if(user == null)
            {
                return Problem(statusCode: 401, title: "Email or Password incorrect");
            }
            var password = _hasher.VerifyHashedPassword(user,user.PasswordHash, request.Password);
            if(password == PasswordVerificationResult.Failed)
            {
                return Problem(statusCode: 401, title: "Email or Password incorrect");
            }
            var (token, expiresAt) = _jwtTokenService.CreateToken(user);
            return Ok(new LoginResponse
            {
                Token = token,
                ExpiresAt = expiresAt
            }
                );
        }
    }
}
