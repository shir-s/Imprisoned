// FILEPATH: Assets/Scripts/Painting/Shapes/StrokePathBuilder.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One "corner" on the simplified stroke path.
/// Stored in LOCAL space of the painted surface, just like StrokeSample.
/// </summary>
public struct StrokeCorner
{
    public int                historyIndex; // index into StrokeHistory
    public Transform          surface;      // same surface as the sample
    public Vector3            localPos;     // corner position in surface local space
    public float              angleDeg;     // measured turn angle (deg)
    public StrokeTurnCategory turn;         // Small / Medium / Sharp etc.

    /// <summary>
    /// Convenience accessor for world position of this corner.
    /// </summary>
    public Vector3 WorldPos
    {
        get
        {
            if (surface == null) return localPos;
            return surface.TransformPoint(localPos);
        }
    }
}

/// <summary>
/// Simplified representation of a closed stroke loop.
/// Right now this just stores the list of detected corners, in
/// drawing order around the loop.
/// </summary>
public class StrokePathLoop
{
    public readonly List<StrokeCorner> corners = new List<StrokeCorner>();

    public int CornerCount => corners.Count;
}

/// <summary>
/// Helpers for building a simplified path (corners) from a StrokeHistory
/// loop segment. This is where we do the generic geometric analysis,
/// so shape detectors do not need to re-implement it.
/// </summary>
public static class StrokePathBuilder
{
    /// <summary>
    /// Build a simplified loop path for the contiguous history segment
    /// [loopStartIndex .. loopEndIndexInclusive].
    ///
    /// CLUSTERING RULE:
    ///   * We classify each index with StrokeTurnUtils.TryGetTurnAt.
    ///   * Any consecutive run of indices whose category is
    ///       Medium or Sharp
    ///     (i.e. NOT separated by Small / None turns)
    ///     becomes ONE "corner cluster".
    ///   * From each cluster we pick the sample with the largest |angle|
    ///     as the representative StrokeCorner.
    ///
    /// So clusters are defined purely by the *sequence* of Medium/Sharp
    /// turn samples, not by distance along the stroke.
    ///
    /// minCornerSeparationMeters is kept only for API compatibility and
    /// is not used for clustering anymore.
    /// </summary>
    public static StrokePathLoop BuildLoopCorners(
        StrokeHistory history,
        int loopStartIndex,
        int loopEndIndexInclusive,
        float minCornerSeparationMeters // currently unused for clustering
    )
    {
        var loop = new StrokePathLoop();

        if (history == null)
            return loop;

        int count = history.Count;
        if (count < 3)
            return loop;

        // Need previous and next samples to compute a turn, so skip
        // the very first and very last samples in the whole history.
        int start = Mathf.Max(loopStartIndex, 1);
        int end   = Mathf.Min(loopEndIndexInclusive, count - 2);

        if (end <= start)
            return loop;

        int window = end - start + 1;

        // Per-index data in the loop window.
        var cats   = new StrokeTurnCategory[window];
        var angles = new float[window];

        for (int i = start; i <= end; i++)
        {
            int localIdx = i - start;

            if (StrokeTurnUtils.TryGetTurnAt(history, i, out float angleDeg, out StrokeTurnCategory cat))
            {
                cats[localIdx]   = cat;
                angles[localIdx] = angleDeg;
            }
            else
            {
                cats[localIdx]   = StrokeTurnCategory.None;
                angles[localIdx] = 0f;
            }
        }

        // Build clusters: maximal contiguous runs of Medium/Sharp samples.
        int currentClusterStart = -1; // local index in [0..window-1]

        for (int localIdx = 0; localIdx < window; localIdx++)
        {
            StrokeTurnCategory cat = cats[localIdx];
            bool isCornerSample    = (cat == StrokeTurnCategory.Medium || cat == StrokeTurnCategory.Sharp);

            if (isCornerSample)
            {
                if (currentClusterStart < 0)
                {
                    // Start a new cluster.
                    currentClusterStart = localIdx;
                }
            }
            else
            {
                // Hit a Small/None turn: close current cluster if any.
                if (currentClusterStart >= 0)
                {
                    AddClusterRepresentative(history, start,
                                             currentClusterStart,
                                             localIdx - 1,
                                             cats, angles, loop);
                    currentClusterStart = -1;
                }
            }
        }

        // If we ended inside a cluster, close it too.
        if (currentClusterStart >= 0)
        {
            AddClusterRepresentative(history, start,
                                     currentClusterStart,
                                     window - 1,
                                     cats, angles, loop);
        }

        return loop;
    }

    /// <summary>
    /// Picks a single representative sample from a cluster and appends
    /// it as a StrokeCorner to the loop.
    ///
    /// The cluster is in local index space [clusterStartLocal .. clusterEndLocal],
    /// where localIndex 0 corresponds to history index = historyStartIndex.
    /// </summary>
    private static void AddClusterRepresentative(
        StrokeHistory history,
        int historyStartIndex,
        int clusterStartLocal,
        int clusterEndLocal,
        StrokeTurnCategory[] cats,
        float[] angles,
        StrokePathLoop loop
    )
    {
        if (clusterEndLocal < clusterStartLocal)
            return;

        // Pick the sample with the largest |angle| in this cluster.
        int   bestLocal = clusterStartLocal;
        float bestMag   = Mathf.Abs(angles[clusterStartLocal]);
        StrokeTurnCategory bestCat = cats[clusterStartLocal];

        for (int local = clusterStartLocal + 1; local <= clusterEndLocal; local++)
        {
            float mag = Mathf.Abs(angles[local]);
            if (mag > bestMag)
            {
                bestMag = mag;
                bestLocal = local;
                bestCat = cats[local];
            }
        }

        int historyIndex = historyStartIndex + bestLocal;
        StrokeSample s   = history[historyIndex];

        var corner = new StrokeCorner
        {
            historyIndex = historyIndex,
            surface      = s.surface,
            localPos     = s.localPos,
            angleDeg     = angles[bestLocal],
            turn         = bestCat
        };

        loop.corners.Add(corner);
    }
}
