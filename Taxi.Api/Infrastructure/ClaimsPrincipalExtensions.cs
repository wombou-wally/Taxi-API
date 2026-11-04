using System.Security.Claims;
using Taxi.Api.Models;

namespace Taxi.Api.Infrastructure
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (value == null || !int.TryParse(value, out int result))
            {
                throw new InvalidOperationException("User id claim is missing or invalid");
            }
            return result;
        }

        public static Role GetUserRole(this ClaimsPrincipal user)
        {
            var role = user.FindFirstValue(ClaimTypes.Role);

            if (!Enum.TryParse<Role>(role, true, out Role result) || !Enum.IsDefined(result))
            {
                throw new InvalidOperationException("Role claim is missing or invalid");
            }
            return result;
        }
    }
}