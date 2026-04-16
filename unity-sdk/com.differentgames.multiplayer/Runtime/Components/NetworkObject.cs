using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    /// <summary>
    /// Her ağ nesnesi (Prefab) üzerinde bulunması gereken kimlik bileşeni.
    /// Nesneyi ağda benzersiz kılan ObjectId ve sahiplik bilgisini tutar.
    /// NetworkBehaviour bileşenleri bu nesneye ihtiyaç duyar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkObject : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private uint _prefabId; // Editor'da prefab'a atanan sabit kimlik

        public NetworkObjectId ObjectId { get; internal set; } = NetworkObjectId.Invalid;
        public NetworkPlayerRef InputAuthority { get; internal set; } = NetworkPlayerRef.None;
        public NetworkRunner Runner { get; internal set; }

        /// <summary>Bu nesnenin prefab referans kimliği (spawn için kullanılır).</summary>
        public uint PrefabId => _prefabId;

        /// <summary>Tüm NetworkBehaviour bileşenlerini cache'ler.</summary>
        internal NetworkBehaviour[] Behaviours { get; private set; }

        private void Awake()
        {
            Behaviours = GetComponents<NetworkBehaviour>();
        }

        /// <summary>Runner bu nesneyi ilk kez spawn ettiğinde çağrılır.</summary>
        internal void NetworkInitialize(NetworkRunner runner, NetworkObjectId id, NetworkPlayerRef owner)
        {
            Runner = runner;
            ObjectId = id;
            InputAuthority = owner;

            foreach (var nb in Behaviours)
            {
                nb.Runner = runner;
            }
        }
    }
}
