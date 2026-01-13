// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeTrailVisualizer.cs

using UnityEngine;

namespace JellyGame.GamePlay.Painting.Trails.Collision
{
    [ExecuteAlways]
    public class StrokeTrailVisualizer : MonoBehaviour
    {
        [SerializeField] private StrokeTrailRecorder recorder;
        [SerializeField] private StrokeCrossingDetector crossingDetector;

        [Header("What to draw")]
        [SerializeField] private bool drawEdgePoints = true;
        [SerializeField] private bool drawCenterPoints = false;

        [Header("Turn colors (no crossing)")]
        [SerializeField] private Color normalColor = Color.green;
        [SerializeField] private Color smallTurnColor = Color.green;
        [SerializeField] private Color mediumTurnColor = Color.magenta;
        [SerializeField] private Color sharpTurnColor = Color.red;

        [Header("Crossing override colors")]
        [SerializeField] private Color crossingSmallColor = Color.cyan;
        [SerializeField] private Color crossingMediumColor = Color.blue;
        [SerializeField] private Color crossingSharpColor = Color.white;

        [SerializeField] private float pointRadius = 0.01f;

        private void OnDrawGizmos()
        {
            if (!recorder) return;

            var history = recorder.History;
            int count = history.Count;
            if (count == 0) return;

            var edgePairs = recorder.EdgePairs;

            for (int i = 0; i < count; i++)
            {
                Color c = normalColor;

                if (StrokeTurnUtils.TryGetTurnAt(history, i, out _, out StrokeTurnCategory turnCat))
                    c = CategoryToColor(turnCat, smallTurnColor, mediumTurnColor, sharpTurnColor);

                if (crossingDetector != null &&
                    crossingDetector.TryGetCrossingCategoryAt(i, out StrokeTurnCategory crossCat))
                    c = CategoryToColor(crossCat, crossingSmallColor, crossingMediumColor, crossingSharpColor);

                Gizmos.color = c;

                Vector3 centerWorld = history[i].WorldPos;

                if (drawEdgePoints && edgePairs != null && i < edgePairs.Count)
                {
                    var p = edgePairs[i];

                    // Draw ONLY the edge points (computed in world each frame so they follow surface tilt)
                    if (p.hasLeft)
                        Gizmos.DrawSphere(p.GetLeftWorld(centerWorld), pointRadius);

                    if (p.hasRight)
                        Gizmos.DrawSphere(p.GetRightWorld(centerWorld), pointRadius);

                    if (!p.hasLeft && !p.hasRight && !drawCenterPoints)
                        Gizmos.DrawSphere(centerWorld, pointRadius);
                }

                if (drawCenterPoints)
                    Gizmos.DrawSphere(centerWorld, pointRadius);
            }
        }

        private static Color CategoryToColor(StrokeTurnCategory cat, Color small, Color medium, Color sharp)
        {
            switch (cat)
            {
                case StrokeTurnCategory.Small: return small;
                case StrokeTurnCategory.Medium: return medium;
                case StrokeTurnCategory.Sharp: return sharp;
                default: return small;
            }
        }
    }
}
