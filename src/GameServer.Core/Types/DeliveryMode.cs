namespace GameServer.Core.Types
{
    public enum DeliveryMode : byte
    {
        /// <summary>
        /// Sent only via UDP, no confirmation expected. Ideal for fast and loss-tolerant data (e.g., position updates).
        /// </summary>
        Unreliable = 0,

        /// <summary>
        /// Ensures that it definitely reaches the other party (ACK), but it is not guaranteed that the packets are processed in order.
        /// </summary>
        ReliableUnordered = 1,

        /// <summary>
        /// Both guaranteed delivery (ACK) and that packets will be received in the order they were sent (Ordering) are guaranteed.
        /// </summary>
        ReliableOrdered = 2
    }
}
