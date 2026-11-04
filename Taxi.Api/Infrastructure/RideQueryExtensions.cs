using Taxi.Api.Models;

namespace Taxi.Api.Infrastructure
{
    public static class RideQueryExtensions
    {
        public static IQueryable<Ride> VisibleTo(this IQueryable<Ride> rides, int userId,Role role)
        {
            switch(role)
            {
                case Role.Customer:
                    return rides.Where(r => r.CustomerId == userId);
                case Role.Driver:
                   return rides.Where(r => r.DriverProfileUserId == userId);
                case Role.Admin:
                    return rides;                 
                default:
                    return rides.Where(r => false);
            } 
        }
    }
}
