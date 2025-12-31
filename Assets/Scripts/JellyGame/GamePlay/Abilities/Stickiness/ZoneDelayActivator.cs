using UnityEngine;
using JellyGame.GamePlay.Abilities.Zones;

namespace JellyGame.GamePlay.Abilities.Zones
{
    [DisallowMultipleComponent]
    public class ZoneDelayActivator : MonoBehaviour
    {
        [SerializeField] private float delaySeconds = 2f;
        [SerializeField] private AbilityZone zone;
        [SerializeField] private bool debugLogs = false;

        private bool _activated = false;
        private float _timer = 0f;

        public void Configure(float delay, AbilityZone targetZone, bool debug = false)
        {
            delaySeconds = delay;
            zone = targetZone;
            debugLogs = debug;
            _timer = delaySeconds;
            
            // Disable colliders initially
            SetCollidersEnabled(false);
        }

        private void Update()
        {
            if (_activated || zone == null)
                return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _activated = true;
                SetCollidersEnabled(true);
                
                if (debugLogs)
                    Debug.Log($"[ZoneDelayActivator] Zone activated after {delaySeconds} seconds.", this);
            }
        }

        private void SetCollidersEnabled(bool enabled)
        {
            MeshCollider[] colliders = GetComponentsInChildren<MeshCollider>();
            foreach (var col in colliders)
            {
                col.enabled = enabled;
            }
        }
    }
}

