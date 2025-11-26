// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeTrailVisualizer.cs
using UnityEngine;

[ExecuteAlways]
public class StrokeTrailVisualizer : MonoBehaviour
{
    [SerializeField] private StrokeTrailRecorder recorder;
    [SerializeField] private StrokeCrossingDetector crossingDetector;

    [Header("Turn colors (no crossing)")]
    [SerializeField] private Color normalColor      = Color.green;
    [SerializeField] private Color smallTurnColor   = Color.green;
    [SerializeField] private Color mediumTurnColor  = Color.magenta;
    [SerializeField] private Color sharpTurnColor   = Color.red;

    [Header("Crossing override colors")]
    [SerializeField] private Color crossingSmallColor  = Color.cyan;
    [SerializeField] private Color crossingMediumColor = Color.blue;
    [SerializeField] private Color crossingSharpColor  = Color.white;

    [SerializeField] private float pointRadius = 0.01f;

    private void OnDrawGizmos()
    {
        if (!recorder) return;

        var history = recorder.History;
        int count = history.Count;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = history[i].WorldPos;
            Color c = normalColor;

            // 1) base color from turn at this point
            if (StrokeTurnUtils.TryGetTurnAt(history, i, out float angle, out StrokeTurnCategory turnCat))
            {
                c = CategoryToColor(turnCat, smallTurnColor, mediumTurnColor, sharpTurnColor);
            }

            // 2) if this point is a crossing, override with crossing color
            if (crossingDetector != null &&
                crossingDetector.TryGetCrossingCategoryAt(i, out StrokeTurnCategory crossCat))
            {
                c = CategoryToColor(crossCat, crossingSmallColor, crossingMediumColor, crossingSharpColor);
            }

            Gizmos.color = c;
            Gizmos.DrawSphere(pos, pointRadius);
        }
    }

    private static Color CategoryToColor(StrokeTurnCategory cat, Color small, Color medium, Color sharp)
    {
        switch (cat)
        {
            case StrokeTurnCategory.Small:  return small;
            case StrokeTurnCategory.Medium: return medium;
            case StrokeTurnCategory.Sharp:  return sharp;
            default:                        return small;
        }
    }
}
