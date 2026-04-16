using GameServer.Core.Interfaces;

namespace GameServer.App.Services
{
    public class ServerLifecycleManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServerLifecycleManager> _logger;

        private CancellationTokenSource? _gameLoopCts;
        private Task? _gameLoopTask;
        private bool _isRunning;
        private readonly object _lock = new object();
        private INetworkRunner? _currentRunner;

        public bool IsRunning
        {
            get
            {
                lock (_lock) return _isRunning;
            }
        }

        public ServerLifecycleManager(IServiceProvider serviceProvider, ILogger<ServerLifecycleManager> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public void StartServer(int port = 7777)
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    _logger.LogWarning("Attempted to start server, but it's already running.");
                    return;
                }

                _logger.LogInformation("Game Server is starting on port {Port}.", port);
                
                var scope = _serviceProvider.CreateScope();
                _currentRunner = scope.ServiceProvider.GetService<INetworkRunner>();

                if (_currentRunner != null)
                {
                    _currentRunner.StartRunner(port);
                    _isRunning = true;
                    _gameLoopCts = new CancellationTokenSource();

                    _gameLoopTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!_gameLoopCts.Token.IsCancellationRequested)
                            {
                                _currentRunner.UpdateLoop();
                                await Task.Delay(10, _gameLoopCts.Token);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected on shutdown
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in Game Server Update Loop.");
                        }
                        finally
                        {
                            // Cleanup
                            _currentRunner.StopRunner();
                            scope.Dispose();
                            _currentRunner = null;
                            lock (_lock)
                            {
                                _isRunning = false;
                            }
                            _logger.LogInformation("UDP Server stopped.");
                        }
                    }, CancellationToken.None);
                }
                else
                {
                    _logger.LogWarning("INetworkRunner service is not registered.");
                    scope.Dispose();
                }
            }
        }

        public void StopServer()
        {
            lock (_lock)
            {
                if (!_isRunning || _gameLoopCts == null)
                {
                    _logger.LogWarning("Attempted to stop server, but it's not running.");
                    return;
                }

                _logger.LogInformation("Game Server stop requested.");
                _gameLoopCts.Cancel();
            }
        }
    }
}
