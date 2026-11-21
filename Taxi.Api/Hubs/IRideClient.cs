namespace Taxi.Api.Hubs
{
    public interface IRideClient
    {
        public Task RideChanged(int rideId, string status);
        public Task AvailableRidesChanged();
    }
}
