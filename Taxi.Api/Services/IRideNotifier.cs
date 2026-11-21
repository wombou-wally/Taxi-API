namespace Taxi.Api.Services
{
    public interface IRideNotifier
    {
        public Task NotifyRideChangedAsync(int rideId, string status, int customerId, int? driverId);
        public Task NotifyAvailableRidesChangedAsync();
    }
}
