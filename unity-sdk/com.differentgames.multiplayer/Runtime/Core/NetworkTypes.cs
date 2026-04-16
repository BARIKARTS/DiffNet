using System;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// A lightweight key type representing a player (or connection) on the network.
    /// Since it's a struct, it lives on the stack and doesn't create GC pressure.
    /// </summary>
    public readonly struct NetworkPlayerRef : IEquatable<NetworkPlayerRef>
    {
        public readonly int Id;
        public static readonly NetworkPlayerRef None = new NetworkPlayerRef(0);
        public static readonly NetworkPlayerRef Server = new NetworkPlayerRef(-1);

        public NetworkPlayerRef(int id) { Id = id; }

        public bool IsNone => Id == 0;
        public bool IsServer => Id == -1;

        public bool Equals(NetworkPlayerRef other) => Id == other.Id;
        public override bool Equals(object obj) => obj is NetworkPlayerRef r && Equals(r);
        public override int GetHashCode() => Id;
        public static bool operator ==(NetworkPlayerRef a, NetworkPlayerRef b) => a.Id == b.Id;
        public static bool operator !=(NetworkPlayerRef a, NetworkPlayerRef b) => a.Id != b.Id;
        public override string ToString() => IsServer ? "Server" : IsNone ? "None" : $"Player[{Id}]";
    }

    /// <summary>
    /// ID identifying a unique object on the network.
    /// </summary>
    public readonly struct NetworkObjectId : IEquatable<NetworkObjectId>
    {
        public readonly uint Value;
        public static readonly NetworkObjectId Invalid = new NetworkObjectId(0);

        public NetworkObjectId(uint value) { Value = value; }

        public bool IsValid => Value != 0;

        public bool Equals(NetworkObjectId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NetworkObjectId n && Equals(n);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(NetworkObjectId a, NetworkObjectId b) => a.Value == b.Value;
        public static bool operator !=(NetworkObjectId a, NetworkObjectId b) => a.Value != b.Value;
        public override string ToString() => $"NetObj[{Value}]";
    }

    /// <summary>
    /// Global Tick counter determined by the server.
    /// The fundamental time unit for deterministic simulation.
    /// </summary>
    public readonly struct NetworkTick : IEquatable<NetworkTick>, IComparable<NetworkTick>
    {
        public readonly int Value;
        public static readonly NetworkTick Invalid = new NetworkTick(-1);

        public NetworkTick(int value) { Value = value; }

        public NetworkTick Next => new NetworkTick(Value + 1);
        public bool IsValid => Value >= 0;

        public bool Equals(NetworkTick other) => Value == other.Value;
        public int CompareTo(NetworkTick other) => Value.CompareTo(other.Value);
        public override bool Equals(object obj) => obj is NetworkTick t && Equals(t);
        public override int GetHashCode() => Value;
        public static bool operator ==(NetworkTick a, NetworkTick b) => a.Value == b.Value;
        public static bool operator !=(NetworkTick a, NetworkTick b) => a.Value != b.Value;
        public static bool operator >(NetworkTick a, NetworkTick b) => a.Value > b.Value;
        public static bool operator <(NetworkTick a, NetworkTick b) => a.Value < b.Value;
        public override string ToString() => $"Tick[{Value}]";
    }

    /// <summary>
    /// Delivery channel used across the SDK. Matches DeliveryMode on the Server side.
    /// </summary>
    public enum DeliveryMode : byte
    {
        Unreliable = 0,
        ReliableUnordered = 1,
        ReliableOrdered = 2
    }
}
