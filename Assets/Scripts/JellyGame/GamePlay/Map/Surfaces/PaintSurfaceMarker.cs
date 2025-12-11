// FILEPATH: Assets/Scripts/Painting/PaintSurfaceMarker.cs

using UnityEngine;

namespace JellyGame.GamePlay.Map.Surfaces
{
    /// <summary>
    /// Add this to any TRIGGER collider you want the brush to be able to "stick" to and paint.
    /// Used by MouseBrushPainter to allow a second raycast that includes triggers,
    /// while ignoring unrelated trigger volumes.
    /// </summary>
    public class PaintSurfaceMarker : MonoBehaviour {}
}
