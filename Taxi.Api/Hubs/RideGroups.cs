namespace Taxi.Api.Hubs
{
    public static class RideGroups
    {

        public const string DriverGroup = "drivers";

        public static string User(int id) => $"user-{id}";
        
    }
}
