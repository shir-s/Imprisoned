// FILEPATH: Assets/Scripts/Abilities/Zones/AbilityZoneContext.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Map.Surfaces;
using UnityEngine;

namespace JellyGame.GamePlay.Abilities.Zones
{
    /// <summary>
    /// Payload passed from shape detection to the currently active ability.
    /// Polygon is in SURFACE LOCAL XZ (x,z -> Vector2(x,z)).
    /// </summary>
    public readonly struct AbilityZoneContext
    {
        public readonly SimplePaintSurface Surface;
        public readonly IReadOnlyList<Vector2> LocalPolygonXZ;
        public readonly Bounds LocalBounds;

        public AbilityZoneContext(SimplePaintSurface surface, IReadOnlyList<Vector2> localPolygonXZ, Bounds localBounds)
        {
            Surface = surface;
            LocalPolygonXZ = localPolygonXZ;
            LocalBounds = localBounds;
        }
    }
}