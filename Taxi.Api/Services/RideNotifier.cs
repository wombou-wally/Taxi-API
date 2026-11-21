using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Taxi.Api.Hubs;

namespace Taxi.Api.Services
{
    public class RideNotifier : IRideNotifier
    {
        private readonly IHubContext<RideHub, IRideClient> _hub;
        private readonly ILogger<RideNotifier> _logger;
        public RideNotifier(IHubContext<RideHub, IRideClient> hubContext, ILogger<RideNotifier> logger)
        {
            _hub = hubContext;
            _logger = logger;
        }
        public async Task NotifyRideChangedAsync(int rideId, string status, int customerId, int? driverId)
        {
            try
            {

                await _hub.Clients.Group(RideGroups.User(customerId)).RideChanged(rideId, status);

                if (driverId.HasValue)
                {
                    await _hub.Clients.Group(RideGroups.User(driverId.Value)).RideChanged(rideId, status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send RideChanged for ride {RideId}", rideId);
            }
        }

        public async Task NotifyAvailableRidesChangedAsync()
        {
            try
            {
                await _hub.Clients.Group(RideGroups.DriverGroup).AvailableRidesChanged();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send AvailableRidesChanged");
            }
        }
    }
}
