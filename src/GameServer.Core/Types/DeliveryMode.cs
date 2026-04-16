namespace GameServer.Core.Types
{
    public enum DeliveryMode : byte
    {
        /// <summary>
        /// Sadece UDP ile gönderilir, onay beklenmez. Hızlı ve kayıplara dayanıklı veriler (örn: pozisyon güncellemeleri) için idealdir.
        /// </summary>
        Unreliable = 0,

        /// <summary>
        /// Karşı tarafa kesinlikle ulaştığından (ACK) emin olunur ancak paketlerin sırasıyla işlenmesi garanti edilmez.
        /// </summary>
        ReliableUnordered = 1,

        /// <summary>
        /// Hem kesin gönderim (ACK) garantilidir hem de paketlerin gönderiliş sırasıyla teslim alınacağı (Ordering) garanti edilir.
        /// </summary>
        ReliableOrdered = 2
    }
}
