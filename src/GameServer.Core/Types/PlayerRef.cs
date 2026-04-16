using System;

namespace GameServer.Core.Types
{
    public readonly struct PlayerRef : IEquatable<PlayerRef>
    {
        public readonly int Id;
        public static PlayerRef None => new(-1);

        public PlayerRef(int id)
        {
            Id = id;
        }

        public bool Equals(PlayerRef other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is PlayerRef other && Equals(other);
        public override int GetHashCode() => Id;

        public static bool operator ==(PlayerRef left, PlayerRef right) => left.Equals(right);
        public static bool operator !=(PlayerRef left, PlayerRef right) => !left.Equals(right);
        
        public override string ToString() => $"[Player:{Id}]";
    }
}
