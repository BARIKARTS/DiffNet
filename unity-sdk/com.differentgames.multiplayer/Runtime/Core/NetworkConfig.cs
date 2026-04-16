using System;

namespace DifferentGames.Multiplayer.Core
{
    [Serializable]
    public struct NetworkConfig
    {
        /// <summary>
        /// Maximum number of [Networked] variables supported per NetworkBehaviour.
        /// Can be 64, 128, 192, or 256. 
        /// Directly affects the BitMask size sent over the network.
        /// </summary>
        public int MaxNetworkedVariables;

        /// <summary>
        /// Size of the ring buffer used for Client-Side Prediction and Delta Compression.
        /// Higher values consume more memory but can tolerate higher latency.
        /// Defaults to 64 ticks (roughly 1 second of data at 60 Hz).
        /// </summary>
        public int StateHistorySize;

        public static NetworkConfig Default => new NetworkConfig
        {
            MaxNetworkedVariables = 64, // 1 ulong (8 bytes) per entity max payload
            StateHistorySize = 64
        };

        internal bool IsValid()
        {
            if (MaxNetworkedVariables <= 0 || MaxNetworkedVariables > 256 || MaxNetworkedVariables % 64 != 0)
                return false;
            
            if (StateHistorySize < 16 || StateHistorySize > 1024)
                return false;

            return true;
        }
        [Space(10)]
        [Header("Interest Management (AOI)")]
        [Tooltip("Enable Grid-based spatial filtering? If disabled, all objects are sent to everyone always.")]
        public bool EnableAOI = true;
        
        [Tooltip("Size of a single grid cell (in Unity units). Used to discretize the world for fast spatial lookups.")]
        public int AOIGridCellSize = 10;
        
        [Tooltip("Radius (in Grid Cells) around a player's cell that they receive updates for. 1 means the 3x3 surrounding cells.")]
        public int AOIGridRadius = 1;
    }
}
