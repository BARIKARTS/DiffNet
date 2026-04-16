namespace GameServer.Core.Metrics
{
    public class NetworkMetrics
    {
        private long _packetsIn;
        private long _packetsOut;
        private long _bytesIn;
        private long _bytesOut;

        public long PacketsIn => Interlocked.Read(ref _packetsIn);
        public long PacketsOut => Interlocked.Read(ref _packetsOut);
        public long BytesIn => Interlocked.Read(ref _bytesIn);
        public long BytesOut => Interlocked.Read(ref _bytesOut);

        public void AddIncomingPacket(int size)
        {
            Interlocked.Increment(ref _packetsIn);
            Interlocked.Add(ref _bytesIn, size);
        }

        public void AddOutgoingPacket(int size)
        {
            Interlocked.Increment(ref _packetsOut);
            Interlocked.Add(ref _bytesOut, size);
        }
    }
}
