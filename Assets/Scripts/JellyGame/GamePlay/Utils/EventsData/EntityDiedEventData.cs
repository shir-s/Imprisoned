// FILEPATH: Assets/Scripts/Core/Events/EntityDiedEventData.cs
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    /// <summary>
    /// Universal death payload (used by SimpleHealth).
    /// Listeners can decide what to do based on layer, tags, components, etc.
    /// </summary>
    public readonly struct EntityDiedEventData
    {
        public readonly GameObject Victim;
        public readonly int VictimLayer;

        public EntityDiedEventData(GameObject victim, int victimLayer)
        {
            Victim = victim;
            VictimLayer = victimLayer;
        }
    }
}