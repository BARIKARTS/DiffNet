using Microsoft.AspNetCore.SignalR;

namespace GameServer.App.Hubs
{
    public class DashboardHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            // İsteğe bağlı olarak admin panele kimin bağlandığı loglanabilir
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            return base.OnDisconnectedAsync(exception);
        }
    }
}
