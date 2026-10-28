using System.Security.Claims;
namespace Taxi.Api.Infrastructure
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user) 
        {
            var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if(value == null || !int.TryParse(value,out int result))
            {
                throw new InvalidOperationException("Faild to find correct user id");
            }
            return result;
        }
    }
}
