using Microsoft.AspNetCore.SignalR;

namespace GameServer.App.Hubs
{
    public class DashboardHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            // Optionally, who is connected to the admin panel can be logged
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            return base.OnDisconnectedAsync(exception);
        }
    }
}
