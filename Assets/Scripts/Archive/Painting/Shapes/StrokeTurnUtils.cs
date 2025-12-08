// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeTurnUtils.cs
using UnityEngine;

public enum StrokeTurnCategory
{
    None,
    Small,
    Medium,
    Sharp
}

public static class StrokeTurnUtils
{
    // Thresholds – tweak to taste
    public const float SmallTurnMax   = 20f;
    public const float MediumTurnMax  = 50f;
    public const float SharpTurnMin   = 50f;

    /// <summary>
    /// Turn at pivot index.
    /// Uses several segments BEFORE and AFTER the pivot and averages them,
    /// so corners that are spread across 2–3 samples still measure close
    /// to the real corner angle.
    /// </summary>
    /// <param name="samplesPerSide">
    /// How many segments on each side to average. 3 is a good default
    /// with your current point spacing.
    /// </param>
    public static bool TryGetTurnAt(
        StrokeHistory history,
        int pivot,
        out float angleDeg,
        out StrokeTurnCategory cat,
        int samplesPerSide = 3)
    {
        angleDeg = 0f;
        cat = StrokeTurnCategory.None;

        if (history == null) return false;
        int count = history.Count;
        if (count < 3) return false;

        // How many segments we *can* actually use on each side
        int maxBack = Mathf.Min(samplesPerSide, pivot);
        int maxFwd  = Mathf.Min(samplesPerSide, count - 1 - pivot);

        if (maxBack < 1 || maxFwd < 1)
            return false;

        Vector3 prevDir = Vector3.zero;
        Vector3 nextDir = Vector3.zero;

        // Average incoming direction (toward the pivot)
        // e.g. (pivot-1 -> pivot) + (pivot-2 -> pivot-1) + ...
        for (int j = 1; j <= maxBack; j++)
        {
            Vector3 newer = history[pivot - (j - 1)].WorldPos;
            Vector3 older = history[pivot - j].WorldPos;
            prevDir += (newer - older);
        }

        // Average outgoing direction (away from the pivot)
        // e.g. (pivot -> pivot+1) + (pivot+1 -> pivot+2) + ...
        for (int j = 1; j <= maxFwd; j++)
        {
            Vector3 older = history[pivot + (j - 1)].WorldPos;
            Vector3 newer = history[pivot + j].WorldPos;
            nextDir += (newer - older);
        }

        if (prevDir.sqrMagnitude < 1e-6f || nextDir.sqrMagnitude < 1e-6f)
            return false;

        angleDeg = Vector3.Angle(prevDir, nextDir);
        cat = Classify(angleDeg);
        return cat != StrokeTurnCategory.None;
    }

    /// <summary>
    /// Turn between two arbitrary indices (used by crossings:
    /// direction near idxA vs direction near idxB).
    /// </summary>
    public static bool TryGetTurnBetween(
        StrokeHistory history,
        int idxA,
        int idxB,
        out float angleDeg,
        out StrokeTurnCategory cat)
    {
        angleDeg = 0f;
        cat = StrokeTurnCategory.None;

        if (history == null || history.Count < 4) return false;
        if (idxA <= 0 || idxA >= history.Count - 1) return false;
        if (idxB <= 0 || idxB >= history.Count - 1) return false;

        Vector3 dirA = history[idxA + 1].WorldPos - history[idxA - 1].WorldPos;
        Vector3 dirB = history[idxB + 1].WorldPos - history[idxB - 1].WorldPos;

        if (dirA.sqrMagnitude < 1e-6f || dirB.sqrMagnitude < 1e-6f) return false;

        angleDeg = Vector3.Angle(dirA, dirB);
        cat = Classify(angleDeg);
        return cat != StrokeTurnCategory.None;
    }

    private static StrokeTurnCategory Classify(float angleDeg)
    {
        float a = Mathf.Abs(angleDeg);

        if (a < SmallTurnMax)  return StrokeTurnCategory.Small;
        if (a < MediumTurnMax) return StrokeTurnCategory.Medium;
        if (a >= SharpTurnMin) return StrokeTurnCategory.Sharp;
        return StrokeTurnCategory.None;
    }
}
