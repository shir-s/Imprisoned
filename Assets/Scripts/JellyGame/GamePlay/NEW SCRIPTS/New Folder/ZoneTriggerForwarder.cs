// FILEPATH: Assets/Scripts/Abilities/Zones/ZoneTriggerForwarder.cs
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Put this on each child trigger collider of the zone.
    /// It forwards trigger events to the AbilityZone root.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZoneTriggerForwarder : MonoBehaviour
    {
        [SerializeField] private AbilityZone zone;

        private void Awake()
        {
            if (zone == null)
                zone = GetComponentInParent<AbilityZone>();
        }

        private void OnTriggerEnter(Collider other)
        {
            zone?.HandleTriggerEnter(other);
        }

        private void OnTriggerExit(Collider other)
        {
            zone?.HandleTriggerExit(other);
        }
    }
}