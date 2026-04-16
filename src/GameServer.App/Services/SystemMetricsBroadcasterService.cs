using System.Diagnostics;
using GameServer.App.Hubs;
using GameServer.Core.Managers;
using GameServer.Core.Metrics;
using Microsoft.AspNetCore.SignalR;

namespace GameServer.App.Services
{
    public class SystemMetricsBroadcasterService : BackgroundService
    {
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly PlayerManager _playerManager;
        private readonly RoomManager _roomManager;
        private readonly NetworkMetrics _networkMetrics;
        private readonly ServerLifecycleManager _lifecycleManager;
        private readonly ILogger<SystemMetricsBroadcasterService> _logger;

        private Process _currentProcess = Process.GetCurrentProcess();
        private DateTime _lastCpuCheckTime;
        private TimeSpan _lastCpuTotalProcessorTime;

        public SystemMetricsBroadcasterService(
            IHubContext<DashboardHub> hubContext,
            PlayerManager playerManager,
            RoomManager roomManager,
            NetworkMetrics networkMetrics,
            ServerLifecycleManager lifecycleManager,
            ILogger<SystemMetricsBroadcasterService> logger)
        {
            _hubContext = hubContext;
            _playerManager = playerManager;
            _roomManager = roomManager;
            _networkMetrics = networkMetrics;
            _lifecycleManager = lifecycleManager;
            _logger = logger;

            _lastCpuCheckTime = DateTime.UtcNow;
            _lastCpuTotalProcessorTime = _currentProcess.TotalProcessorTime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            
            // Keep the last values so we can find the difference (speed/sec)
            long lastBytesIn = _networkMetrics.BytesIn;
            long lastBytesOut = _networkMetrics.BytesOut;
            long lastPacketsIn = _networkMetrics.PacketsIn;
            long lastPacketsOut = _networkMetrics.PacketsOut;

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // 1. Network Metrics (Calculating differences from the last 1 second and converting to KB/s)
                    long currentBytesIn = _networkMetrics.BytesIn;
                    long currentBytesOut = _networkMetrics.BytesOut;
                    long currentPacketsIn = _networkMetrics.PacketsIn;
                    long currentPacketsOut = _networkMetrics.PacketsOut;

                    double bandwidthInKbps = (currentBytesIn - lastBytesIn) / 1024.0;
                    double bandwidthOutKbps = (currentBytesOut - lastBytesOut) / 1024.0;

                    long packetsInPerSec = currentPacketsIn - lastPacketsIn;
                    long packetsOutPerSec = currentPacketsOut - lastPacketsOut;

                    lastBytesIn = currentBytesIn;
                    lastBytesOut = currentBytesOut;
                    lastPacketsIn = currentPacketsIn;
                    lastPacketsOut = currentPacketsOut;

                    // 2. CPU Usage Estimation (as %)
                    var now = DateTime.UtcNow;
                    var cpuTime = _currentProcess.TotalProcessorTime;
                    var timeDelta = now - _lastCpuCheckTime;
                    var cpuDelta = cpuTime - _lastCpuTotalProcessorTime;

                    double cpuUsage = 0;
                    if (timeDelta.TotalMilliseconds > 0)
                    {
                        // Simple CPU percentage calculation (can be divided by proc count for multi-core systems)
                        cpuUsage = (cpuDelta.TotalMilliseconds / timeDelta.TotalMilliseconds) * 100.0 / Environment.ProcessorCount;
                    }

                    _lastCpuCheckTime = now;
                    _lastCpuTotalProcessorTime = cpuTime;

                    // 3. Memory Status (MB) and GC data
                    _currentProcess.Refresh();
                    double memoryUsedMb = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
                    int gen0 = GC.CollectionCount(0);
                    int gen1 = GC.CollectionCount(1);
                    int gen2 = GC.CollectionCount(2);

                    // 4. Data Packet Creation
                    var metricsData = new
                    {
                        timestamp = now.ToString("HH:mm:ss"),
                        isRunning = _lifecycleManager.IsRunning,
                        ccu = _playerManager.CCU,
                        roomCount = _roomManager.ActiveRoomCount,
                        bandwidthInKbps = Math.Round(bandwidthInKbps, 2),
                        bandwidthOutKbps = Math.Round(bandwidthOutKbps, 2),
                        packetsInPerSec,
                        packetsOutPerSec,
                        cpuUsage = Math.Round(cpuUsage, 2),
                        memoryUsedMb = Math.Round(memoryUsedMb, 1),
                        gcGen0 = gen0,
                        gcGen1 = gen1,
                        gcGen2 = gen2
                    };

                    // 5. Broadcoast to Frontend via SignalR
                    await _hubContext.Clients.All.SendAsync("ReceiveMetricsTick", metricsData, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while broadcasting system metrics.");
                }
            }
        }
    }
}
