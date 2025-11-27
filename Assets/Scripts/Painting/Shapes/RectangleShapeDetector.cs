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
/// the missing corner lives at the loop seam (start/end) and synthesize one more
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

    [Tooltip(
        "Extra shift along the surface normal AFTER we align the ramp so that the surface " +
        "cuts through it at Surface Intersection T. Can be negative to push it deeper.")]
    [SerializeField] private float rampHeightOffset = 0.0f;

    [Tooltip(
        "Where the painted surface cuts the ramp along its thickness, in [0..1].\n" +
        "0 = bottom-most corner, 1 = top-most corner along the surface normal.\n" +
        "Use something like 0.7–0.9 so most of the ramp is under the surface.")]
    [Range(0f, 1f)]
    [SerializeField] private float surfaceIntersectionT = 0.8f;

    [Tooltip("Tilt of the ramp in degrees around its local X (right) axis.\n" +
             "Positive values make the ramp go 'up' along the detected forward direction.")]
    [SerializeField] private float rampTiltAngleDeg = 45f;

    [SerializeField] private bool scaleRampToRectangle = true;

    [Header("Z scale tweak")]
    [Tooltip("Z (forward) world scale will be divided by this number after matching rectangle size.\n" +
             "Use this if the model looks too long in Z compared to the drawn rectangle.")]
    [SerializeField] private float zScaleDivider = 5f;

    [Header("Ramp height clamp")]
    [Tooltip("If true, clamp the ramp world height (Y) so it never exceeds maxRampHeight.")]
    [SerializeField] private bool limitRampHeight = true;

    [Tooltip("Maximum world-space height (Y) of spawned ramps.")]
    [SerializeField] private float maxRampHeight = 0.2f;

    [Header("Extra Y rotation")]
    [Tooltip("Additional rotation in degrees around the ramp's local Y axis (after tilt).")]
    [SerializeField] private float extraYRotationDeg = 90f;

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
            {
                Debug.Log(
                    $"[RectangleShapeDetector] Reject: loopLen={loopLen:F3} " +
                    $"outside [{rectangleMinPerimeter:F3},{rectangleMaxPerimeter:F3}].");
            }
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
        // 2) Project corners into 2D and fit a bounding box in (u,v)
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

            float x = Vector3.Dot(rel, u); // along u
            float y = Vector3.Dot(rel, v); // along v

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        float sizeU   = maxX - minX; // we will map this to ramp X (right)
        float sizeV   = maxY - minY; // we will map this to ramp Z (forward)
        float minSide = Mathf.Min(sizeU, sizeV);
        float maxSide = Mathf.Max(sizeU, sizeV);

        if (minSide < rectangleMinSideLength)
        {
            if (debugShapes)
            {
                Debug.Log(
                    $"[RectangleShapeDetector] Reject: side too small (u={sizeU:F3}, v={sizeV:F3}).");
            }
            return false;
        }

        float aspect = maxSide / Mathf.Max(minSide, 0.0001f);
        if (aspect > rectangleMaxAspectRatio)
        {
            if (debugShapes)
            {
                Debug.Log(
                    $"[RectangleShapeDetector] Reject: aspect={aspect:F2} > max={rectangleMaxAspectRatio:F2}.");
            }
            return false;
        }

        float boxPerim = 2f * (sizeU + sizeV);
        if (boxPerim < rectangleMinPerimeter || boxPerim > rectangleMaxPerimeter)
        {
            if (debugShapes)
            {
                Debug.Log(
                    $"[RectangleShapeDetector] Reject: boxPerim={boxPerim:F3} outside allowed range.");
            }
            return false;
        }

        // --- Compute center in 2D and back to world ---
        Vector2 center2D = new Vector2(
            (minX + maxX) * 0.5f,
            (minY + maxY) * 0.5f
        );

        Vector3 centerWS = originWS + u * center2D.x + v * center2D.y;

        // Orientation:
        // - rightWS  = u (maps to local X)
        // - fwdWS    = v (maps to local Z)
        // Scale:
        // - local X world-length = sizeU
        // - local Z world-length = sizeV (then divided by zScaleDivider to "shrink" visually)
        Vector3 rightWS   = u;
        Vector3 forwardWS = v;

        Transform surface = history[startIndex].surface;

        if (debugShapes)
        {
            Debug.Log(
                $"[RectangleShapeDetector] Rectangle ACCEPTED: " +
                $"sizeU={sizeU:F3}, sizeV={sizeV:F3}, aspect={aspect:F2}, " +
                $"corners={cornerCount}, sharpCorners={sharpCorners}, loopLen={loopLen:F3}"
            );
        }

        SpawnRamp(centerWS, avgNormal, forwardWS, rightWS, sizeU, sizeV, surface);
        return true;
    }

    private void SpawnRamp(
        Vector3 centerWS,
        Vector3 normalWS,
        Vector3 forwardWS,
        Vector3 rightWS,
        float sizeAlongRight,
        float sizeAlongForward,
        Transform surface)
    {
        if (!rampPrefab)
            return;

        // 1) Instantiate unparented so parent scale can't distort it
        GameObject ramp = Instantiate(rampPrefab);

        // 2) Scale to match the rectangle footprint in world space (with Z divided)
        if (scaleRampToRectangle)
        {
            float safeDivider = Mathf.Max(zScaleDivider, 0.0001f);

            // Desired world sizes:
            float worldX = sizeAlongRight;
            float worldZ = sizeAlongForward / safeDivider;

            // Height in world = smallest between X and Z
            float worldY = Mathf.Min(worldX, worldZ);

            // Clamp height if requested
            if (limitRampHeight && maxRampHeight > 0f)
            {
                worldY = Mathf.Min(worldY, maxRampHeight);
            }

            Vector3 s;
            s.x = worldX;
            s.z = worldZ;
            s.y = worldY;
            ramp.transform.localScale = s;
        }

        // 3) Set rotation in world space: forward = v, up = surface normal
        Quaternion rot = Quaternion.LookRotation(forwardWS, normalWS);

        // Extra tilt around ramp's right axis
        if (Mathf.Abs(rampTiltAngleDeg) > 0.01f)
        {
            rot = Quaternion.AngleAxis(rampTiltAngleDeg, rightWS) * rot;
        }

        // Extra rotation around local Y (after tilt) – 90° by default
        if (Mathf.Abs(extraYRotationDeg) > 0.01f)
        {
            rot *= Quaternion.Euler(0f, extraYRotationDeg, 0f);
        }

        ramp.transform.rotation = rot;

        // 4) Decide where the surface cuts the ramp along its thickness.
        Vector3 halfExtents = 0.5f * ramp.transform.localScale;

        float minDot = float.PositiveInfinity;
        float maxDot = float.NegativeInfinity;

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 localCorner = new Vector3(
                        sx * halfExtents.x,
                        sy * halfExtents.y,
                        sz * halfExtents.z
                    );

                    Vector3 worldOffset = rot * localCorner;
                    float   dot         = Vector3.Dot(worldOffset, normalWS);

                    if (dot < minDot) minDot = dot;
                    if (dot > maxDot) maxDot = dot;
                }
            }
        }

        float contactDot = Mathf.Lerp(minDot, maxDot, surfaceIntersectionT);
        float d          = -contactDot + rampHeightOffset;

        Vector3 pos = centerWS + normalWS * d;
        ramp.transform.position = pos;

        // 5) Parent to the parent of the painted surface (to avoid cursed non-uniform scale)
        if (surface != null && surface.parent != null)
        {
            ramp.transform.SetParent(surface.parent, worldPositionStays: true);
        }

        if (debugShapes)
        {
            Debug.DrawRay(pos, normalWS * 0.3f, Color.green, 2f);  // surface normal
            Debug.DrawRay(pos, forwardWS * 0.3f, Color.blue,  2f); // uphill direction
            Debug.DrawRay(pos, rightWS   * 0.3f, Color.red,   2f); // ramp right
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

        if (zScaleDivider < 0.0001f) zScaleDivider = 0.0001f;
        if (maxRampHeight < 0f) maxRampHeight = 0f;
    }
}
