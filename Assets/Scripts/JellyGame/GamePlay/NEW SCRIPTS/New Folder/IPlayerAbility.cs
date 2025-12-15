// FILEPATH: Assets/Scripts/Abilities/Zones/IPlayerAbility.cs
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Abilities that react to a filled shape should implement this.
    /// Keep it small: one job = "spawn zone / do something when area is filled".
    /// </summary>
    public interface IPlayerAbility
    {
        Color PaintColor { get; }
        bool CanSpawnZone { get; }

        void SpawnZone(AbilityZoneContext ctx);
    }
}