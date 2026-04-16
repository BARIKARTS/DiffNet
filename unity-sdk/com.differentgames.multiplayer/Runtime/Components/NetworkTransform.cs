using UnityEngine;

namespace DifferentGames.Multiplayer.Components
{
    /// <summary>
    /// A built-in component that automatically handles Transform (position, rotation, scale) synchronization.
    /// The developer adds this class to a GameObject and selects SendRate and Interpolation settings.
    /// This component can be used instead of manually writing [Networked] attributes.
    /// </summary>
    public class NetworkTransform : NetworkBehaviour
    {
        [Header("Sync Settings")]
        [Tooltip("How often should position be sent (per Tick)? (1 = every Tick, 3 = every 3 Ticks)")]
        [SerializeField] private int _sendRateTickInterval = 1;

        [Tooltip("Smooth transition between position and rotation (client-side interpolation)?")]
        [SerializeField] private bool _interpolate = true;

        [Tooltip("Should scale be synchronized?")]
        [SerializeField] private bool _syncScale = false;

        // Networked state
        [Networked(interpolate: true)] public Vector3 NetworkPosition { get; set; }
        [Networked(interpolate: true)] public Quaternion NetworkRotation { get; set; }
        [Networked] public Vector3 NetworkScale { get; set; }

        // Previous and target values for interpolation
        private Vector3 _prevPosition;
        private Quaternion _prevRotation;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        public override void FixedUpdateNetwork()
        {
            // Owner or Server controls the actual logic transform
            if (HasStateAuthority || HasInputAuthority)
            {
                if (CurrentTick.Value % _sendRateTickInterval != 0) return;

                NetworkPosition = transform.position;
                NetworkRotation = transform.rotation;
                if (_syncScale) NetworkScale = transform.localScale;
            }
            else
            {
                // Proxy immediately snaps logic boundaries
                _prevPosition = transform.position;
                _prevRotation = transform.rotation;
                transform.position = NetworkPosition;
                transform.rotation = NetworkRotation;
            }
        }

        public override void Render()
        {
            if (HasStateAuthority) return;

            if (_interpolate)
            {
                if (HasInputAuthority)
                {
                    // Smoothing factor for Rollback corrections (Reconciliation Snap hiding)
                    transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 15f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, NetworkRotation, Time.deltaTime * 15f);
                }
                else
                {
                    // Regular snapshot interpolation for proxies
                    float alpha = Runner != null ? Runner.InterpolationAlpha : 1f;
                    transform.position = Vector3.Lerp(_prevPosition, NetworkPosition, alpha);
                    transform.rotation = Quaternion.Slerp(_prevRotation, NetworkRotation, alpha);
                }
            }
            else
            {
                transform.position = NetworkPosition;
                transform.rotation = NetworkRotation;
            }

            if (_syncScale) transform.localScale = NetworkScale;
        }


        protected override void Awake()
        {
            base.Awake();
            _prevPosition = transform.position;
            _prevRotation = transform.rotation;
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;
        }
    }
}
