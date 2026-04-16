using System;

namespace DifferentGames.Multiplayer.Core
{
    /// <summary>
    /// Zero-allocation, Ring-Buffer based State History.
    /// Allocated exactly ONCE per NetworkBehaviour.
    /// Allows the server to look up past variables (by tick) for Delta Compression,
    /// and allows the client to do Rollback/Prediction.
    /// </summary>
    public class StateHistoryBuffer
    {
        private readonly byte[] _data;
        private readonly int[] _variableOffsets;
        private readonly int _snapshotSize;
        private readonly int _historySize;

        public StateHistoryBuffer(int historySize, int[] variableSizes)
        {
            _historySize = historySize;
            
            _variableOffsets = new int[variableSizes.Length];
            int currentOffset = 0;
            for (int i = 0; i < variableSizes.Length; i++)
            {
                _variableOffsets[i] = currentOffset;
                currentOffset += variableSizes[i];
            }
            
            _snapshotSize = currentOffset;
            
            // Allocate entire history block for this component once
            _data = new byte[_snapshotSize * _historySize];
        }

        public Span<byte> GetVariableData(NetworkTick tick, int variableIndex)
        {
            if (!tick.IsValid || variableIndex >= _variableOffsets.Length) 
                return Span<byte>.Empty;

            int snapshotIndex = tick.Value % _historySize;
            int startOffset = (snapshotIndex * _snapshotSize) + _variableOffsets[variableIndex];
            
            int size = (variableIndex == _variableOffsets.Length - 1) 
                ? _snapshotSize - _variableOffsets[variableIndex] 
                : _variableOffsets[variableIndex + 1] - _variableOffsets[variableIndex];

            return _data.AsSpan(startOffset, size);
        }
    }
}
