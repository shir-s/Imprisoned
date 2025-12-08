// FILEPATH: Assets/Scripts/Movement/TrayRampProfile.cs
using System.Collections.Generic;
using JellyGame.GamePlay.Map;
using UnityEngine;

/// <summary>
/// Describes a single ramp on top of the tray.
/// The cube does NOT use collisions with this. Instead:
/// - KinematicTrayRider queries all TrayRampProfile.ActiveRamps
///   and asks: "Given my tray-local XZ position, am I on this ramp?
///              If yes, what extra height should I have?"
/// - Height over tray is defined by an AnimationCurve.
/// - At the top (u ~ 1), KinematicTrayRider can launch a jump.
///
/// How it works:
/// - We auto-deduce the ramp's base position & direction:
///   originLocalXZ = tray.InverseTransformPoint(transform.position).XZ
///   dirLocalXZ    = projection of transform.forward onto tray local XZ.
/// </summary>
[DisallowMultipleComponent]
public class TrayRampProfile : MonoBehaviour
{
    public static readonly List<TrayRampProfile> ActiveRamps = new List<TrayRampProfile>();

    [Header("Tray Reference")]
    [Tooltip("The tray that this ramp sits on. If empty, will auto-find TiltTray in parents or scene.")]
    [SerializeField] private Transform tray;

    [Header("Ramp Shape (in tray local XZ)")]
    [Tooltip("Length of ramp along its forward direction, in tray local units.")]
    [SerializeField] private float length = 5f;

    [Tooltip("Maximum height above the tray plane at the top of the ramp.")]
    [SerializeField] private float height = 1.5f;

    [Tooltip("0..1 along ramp -> 0..1 height profile. Default is quarter-pipe-ish.")]
    [SerializeField] private AnimationCurve heightCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [Header("Debug")]
    [SerializeField] private bool debugDraw = false;

    Vector2 _originLocalXZ;   // base point of ramp on tray, in tray local XZ
    Vector2 _dirLocalXZ;      // normalized ramp direction in tray local XZ
    bool _initialized;

    public float Length => length;
    public float MaxHeight => height;

    void OnEnable()
    {
        if (!ActiveRamps.Contains(this))
            ActiveRamps.Add(this);

        TryInitialize();
    }

    void OnDisable()
    {
        ActiveRamps.Remove(this);
    }

    void Awake()
    {
        TryInitialize();
    }

    void TryInitialize()
    {
        if (_initialized)
            return;

        if (!tray)
        {
            // Try parent TiltTray, then any TiltTray in scene
            TiltTray parentTray = GetComponentInParent<TiltTray>();
            if (parentTray != null)
            {
                tray = parentTray.transform;
            }
            else
            {
                TiltTray anyTray = FindObjectOfType<TiltTray>();
                if (anyTray != null)
                    tray = anyTray.transform;
            }
        }

        if (!tray)
            return;

        // Origin: ramp base in tray-local XZ
        Vector3 localOrigin = tray.InverseTransformPoint(transform.position);
        _originLocalXZ = new Vector2(localOrigin.x, localOrigin.z);

        // Direction: transform.forward projected into tray local XZ
        Vector3 fwdWorld = transform.forward;
        Vector3 fwdInTray = tray.InverseTransformDirection(
            Vector3.ProjectOnPlane(fwdWorld, tray.up)
        );

        Vector2 dirXZ = new Vector2(fwdInTray.x, fwdInTray.z);
        if (dirXZ.sqrMagnitude < 1e-6f)
            dirXZ = Vector2.up;   // fallback

        _dirLocalXZ = dirXZ.normalized;
        _initialized = true;
    }

    /// <summary>
    /// Sample ramp at a given tray-local XZ position.
    /// Returns:
    ///   true  -> cube is on the ramp segment [0,length], with:
    ///              u        (0..1 position along ramp)
    ///              heightWS (height above tray plane)
    ///              dirLocalXZ (unit direction along ramp in tray local XZ)
    ///   false -> cube is not over this ramp.
    /// </summary>
    public bool SampleHeight(
        Transform trayTransform,
        Vector2 cubeLocalXZ,
        out float u,
        out float heightWS,
        out Vector2 dirLocalXZ
    )
    {
        u = 0f;
        heightWS = 0f;
        dirLocalXZ = Vector2.zero;

        if (!_initialized || trayTransform != tray || length <= 0f)
            return false;

        Vector2 rel = cubeLocalXZ - _originLocalXZ;
        float along = Vector2.Dot(rel, _dirLocalXZ);  // signed distance along ramp dir

        u = along / length;

        if (u < 0f || u > 1f)
        {
            // outside ramp
            return false;
        }

        float t = Mathf.Clamp01(u);
        float hNorm = (heightCurve != null) ? heightCurve.Evaluate(t) : t;
        heightWS = hNorm * height;
        dirLocalXZ = _dirLocalXZ;
        return true;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!debugDraw)
            return;

        if (!tray)
        {
            TiltTray anyTray = FindObjectOfType<TiltTray>();
            if (anyTray != null)
                tray = anyTray.transform;
        }

        if (!tray)
            return;

        TryInitialize();

        // Draw a few samples along the ramp
        Gizmos.color = Color.magenta;

        const int steps = 12;
        Vector3 prev = Vector3.zero;
        bool hasPrev = false;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float u = t;
            float hNorm = (heightCurve != null) ? heightCurve.Evaluate(t) : t;
            float h = hNorm * height;

            float along = u * length;
            Vector2 posLocalXZ = _originLocalXZ + _dirLocalXZ * along;
            Vector3 posLocal = new Vector3(posLocalXZ.x, h, posLocalXZ.y);

            Vector3 posWorld = tray.TransformPoint(posLocal);

            if (hasPrev)
                Gizmos.DrawLine(prev, posWorld);

            prev = posWorld;
            hasPrev = true;
        }
    }
#endif
}
