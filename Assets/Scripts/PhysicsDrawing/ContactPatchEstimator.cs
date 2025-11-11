// FILE: Assets/Scripts/PhysicsDrawing/ContactPatchEstimator.cs
using UnityEngine;

public static class ContactPatchEstimator
{
    // Defaults
    public const float DEFAULT_STEP     = 0.0005f; // 0.5 mm
    public const float DEFAULT_MAXRANGE = 0.03f;   // 3  cm
    public const float DEFAULT_PLANETOL = 0.002f;  // 2  mm

    // Overload WITHOUT optional params (C#-friendly). Calls the full version with defaults.
    public static void EstimateHalfExtents1D(
        Collider coll, Vector3 planePoint, Vector3 planeNormal,
        Vector3 dirA, Vector3 dirB,
        out float halfA, out float halfB)
    {
        EstimateHalfExtents1D(
            coll, planePoint, planeNormal, dirA, dirB,
            DEFAULT_STEP, DEFAULT_MAXRANGE, DEFAULT_PLANETOL,
            out halfA, out halfB);
    }

    // Full version (no optional/default params at the end issue)
    public static void EstimateHalfExtents1D(
        Collider coll, Vector3 planePoint, Vector3 planeNormal,
        Vector3 dirA, Vector3 dirB,
        float step, float maxRange, float planeTol,
        out float halfA, out float halfB)
    {
        halfA = ScanOneAxis(coll, planePoint, planeNormal, dirA, step, maxRange, planeTol);
        halfB = ScanOneAxis(coll, planePoint, planeNormal, dirB, step, maxRange, planeTol);
    }

    static float ScanOneAxis(Collider coll, Vector3 origin, Vector3 n, Vector3 dir, float step, float maxRange, float planeTol)
    {
        float traveledPos = Walk(coll, origin, n, dir, +1f, step, maxRange, planeTol);
        float traveledNeg = Walk(coll, origin, n, dir, -1f, step, maxRange, planeTol);
        return 0.5f * (traveledPos + traveledNeg);
    }

    static float Walk(Collider coll, Vector3 origin, Vector3 n, Vector3 dir, float sign, float step, float maxRange, float planeTol)
    {
        float traveled = 0f;
        int iters = Mathf.CeilToInt(maxRange / step);
        for (int i = 0; i < iters; i++)
        {
            traveled += step;
            Vector3 sample = origin + dir * (sign * traveled);

            // Query slightly off the plane to avoid degeneracy
            Vector3 query = sample + n * 0.0001f;
            Vector3 closest = coll.ClosestPoint(query);

            float distToPlane = Mathf.Abs(Vector3.Dot(closest - origin, n));
            if (distToPlane > planeTol)
                return traveled; // stepped off the footprint
        }
        return maxRange; // capped by search range
    }
}
