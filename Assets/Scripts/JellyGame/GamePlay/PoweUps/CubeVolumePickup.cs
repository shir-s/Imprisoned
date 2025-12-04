using JellyGame.GamePlay.Player;
using UnityEngine;

namespace JellyGame.GamePlay.PoweUps
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class CubeVolumePickup : MonoBehaviour
    {
        [SerializeField] private LayerMask collectorLayers = ~0;
        [SerializeField] private float amount = 0.3f;  
        [SerializeField] private bool destroyOnCollect = true;

        Collider _col;

        void Reset()
        {
            _col = GetComponent<Collider>();
            _col.isTrigger = true;
        }

        void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
                _col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            //check layer
            if ((collectorLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            //find scaler
            var scaler = other.GetComponent<CubeScaler>();
            if (scaler == null)
            {
                Debug.LogWarning("[CubeVolumePickup] Collector has no CubeScaler.", other);
                return;
            }

            // just change volume
            scaler.ChangeVolumeAdd(amount);

            if (destroyOnCollect)
                Destroy(gameObject);
        }
    }
}