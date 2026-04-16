namespace GameServer.Core.Interfaces
{
    public interface INetworkRunner
    {
        int TickRate { get; }
        int CurrentTick { get; }
        bool IsServer { get; }

        void StartRunner(int port);
        void UpdateLoop(); 
        void StopRunner();
    }
}
