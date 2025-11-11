// FILEPATH: Assets/Scripts/PhysicsDrawing/FootprintUtil.cs
using UnityEngine;

public static class FootprintUtil
{
    // penetrationDepth: average positive overlap (meters). 0 = just touching, >0 = pressed in.
    public static float EstimateWidth(Collider coll, Vector3 planePoint, Vector3 planeNormal,
                                      Vector3 strokeTangent, float penetrationDepth,
                                      out Vector3 sideDir)
    {
        Vector3 n = planeNormal.normalized;

        // In-plane basis using stroke direction if possible
        Vector3 t = Vector3.ProjectOnPlane(strokeTangent, n);
        if (t.sqrMagnitude < 1e-8f) t = Vector3.ProjectOnPlane(Vector3.forward, n);
        if (t.sqrMagnitude < 1e-8f) t = Vector3.ProjectOnPlane(Vector3.right,   n);
        t.Normalize();
        sideDir = Vector3.Cross(n, t).normalized;

        switch (coll)
        {
            case SphereCollider sc:  return SphereWidth(sc, planePoint, n, penetrationDepth);
            case CapsuleCollider cc: return CapsuleWidth(cc, planePoint, n, penetrationDepth);
            case BoxCollider bc:     return BoxWidth(bc, sideDir);
            default:                 return BoundsWidth(coll, sideDir);
        }
    }

    static float SphereWidth(SphereCollider sc, Vector3 planePoint, Vector3 n, float pen)
    {
        // r_in_plane from penetration: r_ip = sqrt(max(0, 2*r*pen - pen^2))
        Transform tr = sc.transform;
        float uni = Mathf.Max(Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.y), Mathf.Abs(tr.lossyScale.z));
        float r = sc.radius * uni;
        float r_ip = Mathf.Sqrt(Mathf.Max(0f, 2f * r * pen - pen * pen));
        return 2f * r_ip;
    }

    static float CapsuleWidth(CapsuleCollider cc, Vector3 planePoint, Vector3 n, float pen)
    {
        Transform tr = cc.transform;

        // World axis
        Vector3 axisLocal =
            cc.direction == 0 ? Vector3.right :
            cc.direction == 1 ? Vector3.up    : Vector3.forward;
        Vector3 axis = tr.TransformDirection(axisLocal).normalized;

        // Effective radius & height with scale
        Vector3 s = tr.lossyScale;
        float rScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        float r = cc.radius * rScale;

        float hScale =
            cc.direction == 0 ? Mathf.Abs(s.x) :
            cc.direction == 1 ? Mathf.Abs(s.y) : Mathf.Abs(s.z);
        float h = cc.height * hScale;

        // Orientation factor: 0 = tip, 1 = side
        float sideFactor = 1f - Mathf.Abs(Vector3.Dot(axis, n));

        // “Tip” model (end-cap sphere): patch radius driven by penetration
        float tip_r_ip = Mathf.Sqrt(Mathf.Max(0f, 2f * r * pen - pen * pen)); // 0..r

        // “Side” model (cylindrical body): tends to 2r, but scale with pressure so it’s not always max
        // When just grazing (pen ~ 0), let width be small; as pen grows, approach 2r.
        float side_r_ip = Mathf.Lerp(0f, r, Mathf.Clamp01(pen / (0.5f * r))); // reach r around 0.5r penetration

        float r_ip = Mathf.Lerp(tip_r_ip, side_r_ip, sideFactor);

        // If cylinder actually has length, keep a floor when sliding side-on
        if (sideFactor > 0.7f && h > 2f * r)
            r_ip = Mathf.Max(r_ip, 0.6f * r);

        return 2f * Mathf.Clamp(r_ip, 0f, r);
    }

    static float BoxWidth(BoxCollider bc, Vector3 sideDir)
    {
        Transform tr = bc.transform;
        Vector3 hx = tr.TransformVector(new Vector3(bc.size.x * 0.5f, 0, 0));
        Vector3 hy = tr.TransformVector(new Vector3(0, bc.size.y * 0.5f, 0));
        Vector3 hz = tr.TransformVector(new Vector3(0, 0, bc.size.z * 0.5f));

        float half = Mathf.Abs(Vector3.Dot(hx, sideDir)) +
                     Mathf.Abs(Vector3.Dot(hy, sideDir)) +
                     Mathf.Abs(Vector3.Dot(hz, sideDir));
        return 2f * half;
    }

    static float BoundsWidth(Collider c, Vector3 sideDir)
    {
        Bounds b = c.bounds;
        Vector3 ex = new Vector3(b.extents.x, 0, 0);
        Vector3 ey = new Vector3(0, b.extents.y, 0);
        Vector3 ez = new Vector3(0, 0, b.extents.z);
        float half = Mathf.Abs(Vector3.Dot(ex, sideDir)) +
                     Mathf.Abs(Vector3.Dot(ey, sideDir)) +
                     Mathf.Abs(Vector3.Dot(ez, sideDir));
        return 2f * half;
    }
}
