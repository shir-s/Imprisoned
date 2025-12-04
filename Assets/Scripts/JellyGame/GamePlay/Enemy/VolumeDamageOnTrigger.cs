using JellyGame.GamePlay.Player;
using UnityEngine;

namespace JellyGame.GamePlay.Enemy
{
    [DisallowMultipleComponent]
    public class VolumeDamageOnTrigger : MonoBehaviour
    {
        [SerializeField] private LayerMask targetLayers = ~0;

        [Tooltip("How much volume to remove (must be a negative value). For example: -0.3f")]
        [SerializeField] private float volumeDelta = -0.3f;

        private void OnTriggerEnter(Collider other)
        {
            // Only react to objects on the allowed layers
            if ((targetLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            var scaler = other.GetComponent<CubeScaler>();
            if (scaler == null)
                return;

            scaler.ChangeVolumeAdd(volumeDelta);

            // We do NOT destroy the trap — it can keep damaging the cube
        }
    }
}