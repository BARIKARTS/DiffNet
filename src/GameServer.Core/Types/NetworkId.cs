using System;

namespace GameServer.Core.Types
{
    public readonly struct NetworkId : IEquatable<NetworkId>
    {
        public readonly uint Value;
        public static NetworkId None => new(0);

        public NetworkId(uint value)
        {
            Value = value;
        }

        public bool Equals(NetworkId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is NetworkId other && Equals(other);
        public override int GetHashCode() => (int)Value;

        public static bool operator ==(NetworkId left, NetworkId right) => left.Equals(right);
        public static bool operator !=(NetworkId left, NetworkId right) => !left.Equals(right);

        public override string ToString() => $"[NetId:{Value}]";
    }
}
