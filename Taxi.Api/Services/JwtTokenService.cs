using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Taxi.Api.Models;

namespace Taxi.Api.Services
{
    public class JwtTokenService
    {
        private readonly IConfiguration _config;
        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }
        public (string Token, DateTime ExpiresAt) CreateToken(User user)
        {
            var key = _config["Jwt:Key"];
            var issuer = _config["Jwt:Issuer"];
            var audience = _config["Jwt:Audience"];
            var expiry = _config["Jwt:ExpiryMinutes"];

            if (string.IsNullOrEmpty(key) || key.Length < 32)
            {
                throw new InvalidOperationException("Jwt Key missing or shorter than expected");
            }
            if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
            {
                throw new InvalidOperationException("Issuer or Audience missing");
            }


            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString())

            };
            var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var signingCredentials = new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256);
            int expiryDuration = int.TryParse(expiry,out int result) ? result : throw new InvalidOperationException("Jwt:ExpiryMinutes is missing or not a number");

            var expiresAt = DateTime.UtcNow.AddMinutes(expiryDuration);

            var jwt = new JwtSecurityToken(issuer: issuer, audience: audience, claims: claims, expires: expiresAt, signingCredentials: signingCredentials);
            var token = new JwtSecurityTokenHandler().WriteToken(jwt);
            return (token, expiresAt);

        }
    }
}
