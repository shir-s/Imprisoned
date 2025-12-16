// FILEPATH: Assets/Scripts/Abilities/Zones/IZoneEffect.cs
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Effects live on the spawned zone object (composition).
    /// Zone just forwards enter/exit/stay/ticks to these.
    /// </summary>
    public interface IZoneEffect
    {
        void OnZoneSpawned(AbilityZone zone);
        void OnZoneDespawned(AbilityZone zone);

        void OnTargetEntered(Collider other);
        void OnTargetExited(Collider other);

        /// <summary>Called at a fixed-ish tick rate by the zone (optional).</summary>
        void Tick(float deltaTime);
    }
}