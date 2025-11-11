// FILEPATH: Assets/Scripts/Gate/ForceGate.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class ForceGate : MonoBehaviour
{
    [Header("Blocking")]
    [Tooltip("Layers that this gate resists. Any Rigidbody on these layers will be pushed back.")]
    [SerializeField] private LayerMask blockLayers;

    [Tooltip("Direction the gate resists, in world space. If left zero, uses this transform's forward.")]
    [SerializeField] private Vector3 overrideBlockDirectionWS = Vector3.zero;

    [Tooltip("Base opposing force (Newtons as ForceMode.Force). This is reduced by painting.")]
    [SerializeField] private float baseOpposeForce = 300f;

    [Tooltip("Below this force, the gate effectively can’t stop passage anymore.")]
    [SerializeField] private float minForceToStop = 25f;

    [Header("Painting → Weakening")]
    [Tooltip("How many meters of drawing are needed to reduce the gate from base to zero force.")]
    [SerializeField] private float metersToFullyOpen = 8f;

    [Tooltip("If true, painting reduces strength globally (anywhere on the trigger). If false, we weaken only near painted points.")]
    [SerializeField] private bool globalWeakening = true;

    [Tooltip("Radius (meters) for local weakening around each paint hit when globalWeakening = false.")]
    [SerializeField] private float localWeakeningRadius = 1.0f;

    [Tooltip("Falloff sharpness for local weakening (higher = tighter blob).")]
    [SerializeField] private float localFalloffSharpness = 3.0f;

    [Tooltip("Max number of local paint blobs to keep (older/weak ones get replaced).")]
    [SerializeField] private int maxLocalBlobs = 64;

    [Header("Physics")]
    [Tooltip("Continuous push mode. Force is applied every FixedUpdate while a body is inside.")]
    [SerializeField] private ForceMode forceMode = ForceMode.Force;

    [Tooltip("Multiply force by Rigidbody mass (useful when using Acceleration).")]
    [SerializeField] private bool scaleByMass = false;

    [Header("Debug")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(1, 0.4f, 0.2f, 0.15f);
    [SerializeField] private Color blobGizmoColor = new Color(1, 0.1f, 0.1f, 0.25f);

    // ---- internal state ----
    private BoxCollider _col;

    // Global accumulation (when globalWeakening = true)
    private float _paintAccumMeters;

    // Local accumulation (when globalWeakening = false): small set of “blobs”
    private struct Blob
    {
        public Vector3 posWS;   // representative center (column axis runs along BlockDirWS)
        public float   meters;  // accumulated meters (0..metersToFullyOpen)
    }
    private readonly List<Blob> _blobs = new List<Blob>(32);

    void Awake()
    {
        _col = GetComponent<BoxCollider>();
        _col.isTrigger = true; // force volume

        if (metersToFullyOpen < 0.001f) metersToFullyOpen = 0.001f;
        if (baseOpposeForce < 0f) baseOpposeForce = 0f;
        if (minForceToStop < 0f) minForceToStop = 0f;
        if (maxLocalBlobs < 1) maxLocalBlobs = 1;
        if (localWeakeningRadius < 1e-4f) localWeakeningRadius = 1e-4f;
        if (localFalloffSharpness < 1e-3f) localFalloffSharpness = 1e-3f;
    }

    private Vector3 BlockDirWS()
    {
        if (overrideBlockDirectionWS.sqrMagnitude > 1e-8f)
            return overrideBlockDirectionWS.normalized;
        return transform.forward; // default
    }

    // Lateral (planar) distance ignoring depth along the block direction → creates a column through thickness
    private float LateralDistance(Vector3 aWS, Vector3 bWS, Vector3 blockDir)
    {
        Vector3 d = aWS - bWS;
        // remove the component along blockDir (depth), keep only in-plane offset
        Vector3 lateral = d - Vector3.Dot(d, blockDir) * blockDir;
        return lateral.magnitude;
    }

    // Move a point toward a target, but only along the lateral plane (no movement along blockDir)
    private Vector3 LateralFollow(Vector3 from, Vector3 toward, Vector3 blockDir, float t)
    {
        Vector3 delta = toward - from;
        Vector3 lateral = delta - Vector3.Dot(delta, blockDir) * blockDir;
        return from + lateral * Mathf.Clamp01(t);
    }

    /// <summary>
    /// Called by MouseBrushPainter when the brush paints on this gate.
    /// </summary>
    public void AddPaint(Vector3 worldPoint, float brushRadius, float metersDrawn)
    {
        if (metersDrawn <= 0f) return;

        if (globalWeakening)
        {
            _paintAccumMeters = Mathf.Min(metersToFullyOpen, _paintAccumMeters + metersDrawn);
            return;
        }

        Vector3 push = BlockDirWS();

        // --- Localized mode: deposit into nearest blob in *lateral* space (columnar opening) ---
        float mergeDist = Mathf.Max(brushRadius, localWeakeningRadius * 0.5f);
        int bestIdx = -1;
        float bestDist = float.PositiveInfinity;

        for (int i = 0; i < _blobs.Count; i++)
        {
            float d = LateralDistance(worldPoint, _blobs[i].posWS, push);
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0 && bestDist <= mergeDist)
        {
            var b = _blobs[bestIdx];
            b.meters = Mathf.Min(metersToFullyOpen, b.meters + metersDrawn);
            // Follow only laterally so the blob represents a vertical column along push direction
            b.posWS = LateralFollow(b.posWS, worldPoint, push, 0.35f);
            _blobs[bestIdx] = b;
        }
        else
        {
            var newBlob = new Blob { posWS = worldPoint, meters = Mathf.Min(metersToFullyOpen, metersDrawn) };
            if (_blobs.Count < maxLocalBlobs)
            {
                _blobs.Add(newBlob);
            }
            else
            {
                int weakest = 0;
                float minMeters = _blobs[0].meters;
                for (int i = 1; i < _blobs.Count; i++)
                {
                    if (_blobs[i].meters < minMeters)
                    {
                        minMeters = _blobs[i].meters;
                        weakest = i;
                    }
                }
                _blobs[weakest] = newBlob;
            }
        }
    }

    /// <summary>
    /// Current opposing force (0..base), reduced by painting.
    /// </summary>
    public float CurrentOpposeForce(Vector3 samplePointWS)
    {
        if (globalWeakening)
        {
            float t = Mathf.Clamp01(_paintAccumMeters / metersToFullyOpen); // 0..1
            return Mathf.Max(0f, baseOpposeForce * (1f - t));
        }
        else
        {
            if (_blobs.Count == 0) return baseOpposeForce;

            Vector3 push = BlockDirWS();
            float sigma2 = Mathf.Max(1e-6f, (localWeakeningRadius * localWeakeningRadius) / localFalloffSharpness);

            float reduction = 0f; // 0..1
            for (int i = 0; i < _blobs.Count; i++)
            {
                float distLateral = LateralDistance(samplePointWS, _blobs[i].posWS, push);
                float w = Mathf.Exp(-(distLateral * distLateral) / (2f * sigma2)); // 0..1
                float tLocal = Mathf.Clamp01(_blobs[i].meters / metersToFullyOpen); // 0..1
                reduction += tLocal * w;
            }

            reduction = Mathf.Clamp01(reduction);
            float force = baseOpposeForce * (1f - reduction);
            return Mathf.Max(0f, force);
        }
    }

    void OnTriggerStay(Collider other)
    {
        // Only act on bodies on the selected layers
        if (((1 << other.gameObject.layer) & blockLayers) == 0) return;

        var rb = other.attachedRigidbody;
        if (!rb) return;

        Vector3 pushDir = BlockDirWS();
        float opposeForce = CurrentOpposeForce(other.transform.position);

        // If still strong enough to stop
        if (opposeForce >= minForceToStop)
        {
            float vDot = Vector3.Dot(rb.velocity, pushDir);

            float forceToApply = opposeForce;
            if (scaleByMass && (forceMode == ForceMode.Force || forceMode == ForceMode.Acceleration))
                forceToApply *= rb.mass;

            Vector3 forceWS = -pushDir * (forceToApply * Mathf.Max(0.5f, 1f + Mathf.Max(0f, vDot)));

            rb.AddForce(forceWS, forceMode);

            // small nudge out of the volume to reduce jitter
            rb.position += -pushDir * 0.0001f;
        }
        // else: too weak to stop → passage allowed at this local column
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;
        var col = GetComponent<BoxCollider>();
        if (!col) return;

        // gate volume
        Gizmos.color = gizmoColor;
        Matrix4x4 m = Matrix4x4.TRS(transform.TransformPoint(col.center), transform.rotation, Vector3.one);
        Gizmos.matrix = m;
        Gizmos.DrawCube(Vector3.zero, col.size);

        // direction arrow
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 p = transform.TransformPoint(col.center);
        Vector3 dir = (overrideBlockDirectionWS.sqrMagnitude > 1e-8f ? overrideBlockDirectionWS.normalized : transform.forward);
        Gizmos.color = new Color(1, 0.2f, 0.1f, 1f);
        Gizmos.DrawLine(p, p + dir * 0.75f);
        Gizmos.DrawSphere(p + dir * 0.75f, 0.03f);

#if UNITY_EDITOR
        // local blobs visualization
        if (!Application.isPlaying || _blobs.Count > 0)
        {
            Gizmos.color = blobGizmoColor;
            foreach (var b in _blobs)
            {
                // draw a small sphere at the blob center (represents the column axis)
                Gizmos.DrawSphere(b.posWS, Mathf.Min(0.05f, localWeakeningRadius * 0.2f));
            }
        }
#endif
    }
}
