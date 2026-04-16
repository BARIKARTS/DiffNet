namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Base interface for unmanaged structs containing player inputs.
    /// Custom Input structs must implement this to be compatible with NetworkRunner.
    /// Note: Input structs are strictly limited to MAXIMUM 64 Bytes to preserve Zero-Allocation constraints.
    /// </summary>
    public interface INetworkInput
    {
    }
}
