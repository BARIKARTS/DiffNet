using GameServer.Core.Interfaces;

namespace GameServer.App.Services
{
    public class GameServerHostedService : BackgroundService
    {
        private readonly ServerLifecycleManager _lifecycleManager;
        private readonly ILogger<GameServerHostedService> _logger;

        public GameServerHostedService(ServerLifecycleManager lifecycleManager, ILogger<GameServerHostedService> logger)
        {
            _lifecycleManager = lifecycleManager;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GameServerHostedService background task is starting.");
            
            // Start the server automatically on boot
            _lifecycleManager.StartServer(7777);

            // Wait until cancellation is requested
            var tcs = new TaskCompletionSource();
            stoppingToken.Register(() => tcs.SetResult());
            return tcs.Task;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GameServerHostedService background task is stopping.");
            _lifecycleManager.StopServer();
            await base.StopAsync(cancellationToken);
        }
    }
}
