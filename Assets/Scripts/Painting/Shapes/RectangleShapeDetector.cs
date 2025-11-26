// FILEPATH: Assets/Scripts/Painting/Shapes/RectangleShapeDetector.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shape detector:
/// - Receives a closed loop segment (with precomputed StrokePathLoop) from StrokeTrailAnalyzer.
/// - Uses the corners (Medium/Sharp turn representatives) as its "corner clusters".
/// - If we end up with the right number of corners (usually 4),
///   and their projected bounding box looks rectangle-ish (aspect, size),
///   we spawn a ramp aligned to that rectangle.
/// 
/// Note: if we get exactly (requiredCornerClusters - 1) corners, we assume
/// the missing corner is at the loop seam (start/end) and synthesize one more
/// corner at endIndexInclusive.
/// 
/// Attach to the SAME GameObject as StrokeTrailAnalyzer + StrokeTrailRecorder.
/// </summary>
[DisallowMultipleComponent]
public class RectangleShapeDetector : MonoBehaviour, IStrokeShapeDetector
{
    [Header("Basic size / perimeter")]
    [SerializeField] private float rectangleMinPerimeter   = 0.3f;
    [SerializeField] private float rectangleMaxPerimeter   = 60.0f;
    [SerializeField] private float rectangleMinSideLength  = 0.10f;
    [SerializeField] private float rectangleMaxAspectRatio = 4f; // longerSide / shorterSide

    [Header("Corner-based rectangle filter")]
    [Tooltip("How many corner CLUSTERS we expect for a rectangle. 4 = strict rectangle.")]
    [SerializeField] private int requiredCornerClusters = 4;

    [Tooltip("At least this many corners must be SHARP.")]
    [SerializeField] private int minSharpCornerClusters = 2;

    [Header("Rectangle → Ramp")]
    [SerializeField] private GameObject rampPrefab;
    [SerializeField] private float rampHeightOffset = 0.01f;
    [SerializeField] private bool scaleRampToRectangle = true;

    [Header("Debug")]
    [SerializeField] private bool debugShapes = true;

    public bool TryHandleShape(StrokeLoopSegment loopSegment)
    {
        StrokeHistory history           = loopSegment.history;
        int           startIndex        = loopSegment.startIndex;
        int           endIndexInclusive = loopSegment.endIndexInclusive;
        StrokePathLoop path             = loopSegment.path;

        if (!rampPrefab)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] No rampPrefab assigned, ignoring.", this);
            return false;
        }

        if (history == null || history.Count < 6)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] Reject: history too short.");
            return false;
        }

        int count = history.Count;
        if (startIndex < 0 || endIndexInclusive >= count || endIndexInclusive <= startIndex + 2)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: bad indices start={startIndex}, end={endIndexInclusive}, count={count}.");
            return false;
        }

        // --- Perimeter check on this loop segment ---
        float totalLen   = history.GetLengthAt(endIndexInclusive);
        float beforeLoop = (startIndex > 0) ? history.GetLengthAt(startIndex) : 0f;
        float loopLen    = totalLen - beforeLoop;

        if (loopLen < rectangleMinPerimeter || loopLen > rectangleMaxPerimeter)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: loopLen={loopLen:F3} outside [{rectangleMinPerimeter:F3},{rectangleMaxPerimeter:F3}].");
            return false;
        }

        // --- Average normal over this loop (for surface plane) ---
        Vector3 avgNormal = Vector3.zero;
        for (int i = startIndex; i <= endIndexInclusive; i++)
            avgNormal += history[i].WorldNormal;

        avgNormal.Normalize();
        if (avgNormal.sqrMagnitude < 0.25f)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] Reject: avgNormal too small / unstable.");
            return false;
        }

        // --- Build a local 2D basis on the surface plane ---
        Vector3 u = Vector3.Cross(Vector3.up, avgNormal);
        if (u.sqrMagnitude < 1e-6f)
            u = Vector3.Cross(Vector3.right, avgNormal);
        u.Normalize();
        Vector3 v = Vector3.Cross(avgNormal, u).normalized;

        // We'll use the first corner (or first history sample) as origin.
        Vector3 fallbackOrigin = history[startIndex].WorldPos;

        // --------------------------------------------------------------------
        // 1) Take corners from precomputed StrokePathLoop (analyzer)
        // --------------------------------------------------------------------
        List<StrokeCorner> corners = (path != null) ? path.corners : null;
        int cornerCount = (corners != null) ? corners.Count : 0;

        if (debugShapes)
        {
            Debug.Log($"[RectangleShapeDetector] Precomputed corners from analyzer: {cornerCount}.");
        }

        if (corners == null)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] Reject: no corner path provided.");
            return false;
        }

        // Count SHARP corners
        int sharpCorners = 0;
        for (int i = 0; i < cornerCount; i++)
        {
            if (corners[i].turn == StrokeTurnCategory.Sharp)
                sharpCorners++;
        }

        // --------------------------------------------------------------------
        // 1.5) Seam corner: if we are missing exactly one corner, assume
        //      the last corner lives at the loop closure (start/end).
        // --------------------------------------------------------------------
        if (cornerCount == requiredCornerClusters - 1)
        {
            StrokeSample seamSample = history[endIndexInclusive];

            StrokeCorner seamCorner = new StrokeCorner
            {
                historyIndex = endIndexInclusive,
                surface      = seamSample.surface,
                localPos     = seamSample.localPos,
                angleDeg     = 90f,                         // treat seam as a strong corner
                turn         = StrokeTurnCategory.Sharp
            };

            corners.Add(seamCorner);
            cornerCount = corners.Count;

            // recompute sharp count
            sharpCorners = 0;
            for (int i = 0; i < cornerCount; i++)
            {
                if (corners[i].turn == StrokeTurnCategory.Sharp)
                    sharpCorners++;
            }

            if (debugShapes)
            {
                Debug.Log(
                    $"[RectangleShapeDetector] Added seam corner at index {endIndexInclusive}. " +
                    $"Now: totalCorners={cornerCount}, sharpCorners={sharpCorners}");
            }
        }

        // --- Basic corner sanity checks ---
        if (cornerCount != requiredCornerClusters)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: expected {requiredCornerClusters} corners, got {cornerCount}.");
            return false;
        }

        if (sharpCorners < minSharpCornerClusters)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] Reject: not enough SHARP corners.");
            return false;
        }

        if (cornerCount == 0)
        {
            if (debugShapes)
                Debug.Log("[RectangleShapeDetector] Reject: no corners at all.");
            return false;
        }

        // --------------------------------------------------------------------
        // 2) Project corners into 2D and fit a bounding box
        // --------------------------------------------------------------------
        Vector3 originWS = corners[0].WorldPos;
        if (originWS.sqrMagnitude == 0f) // very paranoid fallback
            originWS = fallbackOrigin;

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < cornerCount; i++)
        {
            Vector3 pWS = corners[i].WorldPos;
            Vector3 rel = pWS - originWS;

            float x = Vector3.Dot(rel, u);
            float y = Vector3.Dot(rel, v);

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        float width  = maxX - minX;
        float height = maxY - minY;
        float minSide = Mathf.Min(width, height);
        float maxSide = Mathf.Max(width, height);

        if (minSide < rectangleMinSideLength)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: side too small (w={width:F3}, h={height:F3}).");
            return false;
        }

        float aspect = maxSide / Mathf.Max(minSide, 0.0001f);
        if (aspect > rectangleMaxAspectRatio)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: aspect={aspect:F2} > max={rectangleMaxAspectRatio:F2}.");
            return false;
        }

        float boxPerim = 2f * (width + height);
        if (boxPerim < rectangleMinPerimeter || boxPerim > rectangleMaxPerimeter)
        {
            if (debugShapes)
                Debug.Log($"[RectangleShapeDetector] Reject: boxPerim={boxPerim:F3} outside allowed range.");
            return false;
        }

        // --- Compute center in 2D and back to world ---
        Vector2 center2D = new Vector2(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f
        );

        Vector3 centerWS = originWS + u * center2D.x + v * center2D.y;

        // Decide ramp orientation: longer side = "forward"
        Vector3 forwardWS, rightWS;
        if (height >= width)
        {
            forwardWS = v;
            rightWS   = u;
        }
        else
        {
            forwardWS = u;
            rightWS   = v;
        }

        Transform surface = history[startIndex].surface;

        if (debugShapes)
        {
            Debug.Log(
                $"[RectangleShapeDetector] Rectangle ACCEPTED: " +
                $"w={width:F3}, h={height:F3}, aspect={aspect:F2}, " +
                $"corners={cornerCount}, sharpCorners={sharpCorners}, loopLen={loopLen:F3}"
            );
        }

        SpawnRamp(centerWS, avgNormal, forwardWS, rightWS, width, height, surface);
        return true;
    }

    private void SpawnRamp(
        Vector3 centerWS,
        Vector3 normalWS,
        Vector3 forwardWS,
        Vector3 rightWS,
        float width,
        float height,
        Transform surface)
    {
        if (!rampPrefab)
            return;

        Quaternion rot = Quaternion.LookRotation(forwardWS, normalWS);
        Vector3 pos = centerWS + normalWS * rampHeightOffset;

        GameObject ramp = Instantiate(rampPrefab, pos, rot);

        if (scaleRampToRectangle)
        {
            Vector3 s = ramp.transform.localScale;
            s.x = width;
            s.z = height;
            ramp.transform.localScale = s;
        }

        if (surface != null)
        {
            ramp.transform.SetParent(surface, worldPositionStays: true);
        }

        if (debugShapes)
        {
            Debug.DrawRay(pos, normalWS * 0.3f, Color.green, 2f);
            Debug.DrawRay(pos, forwardWS * 0.3f, Color.blue,  2f);
            Debug.DrawRay(pos, rightWS   * 0.3f, Color.red,   2f);
        }
    }

    private void OnValidate()
    {
        if (rectangleMinPerimeter < 0.01f) rectangleMinPerimeter = 0.01f;
        if (rectangleMaxPerimeter < rectangleMinPerimeter) rectangleMaxPerimeter = rectangleMinPerimeter;
        if (rectangleMinSideLength < 0.001f) rectangleMinSideLength = 0.001f;
        if (rectangleMaxAspectRatio < 1f) rectangleMaxAspectRatio = 1f;

        if (requiredCornerClusters < 3) requiredCornerClusters = 3;
        if (minSharpCornerClusters < 0) minSharpCornerClusters = 0;
        if (minSharpCornerClusters > requiredCornerClusters)
            minSharpCornerClusters = requiredCornerClusters;
    }
}
