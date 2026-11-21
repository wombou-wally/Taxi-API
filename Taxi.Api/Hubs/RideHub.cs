using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Taxi.Api.Infrastructure;
using Taxi.Api.Models;

namespace Taxi.Api.Hubs
{
    [Authorize]
    public class RideHub : Hub<IRideClient>
    {
        public override async Task OnConnectedAsync()
        {
            
            int id = Context.User.GetUserId();
            await Groups.AddToGroupAsync(Context.ConnectionId,RideGroups.User(id));
            var role = Context.User.GetUserRole();
            if(role == Role.Driver)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, RideGroups.DriverGroup);
            }
            await base.OnConnectedAsync();
        }
    }
}
