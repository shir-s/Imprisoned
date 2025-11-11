// FILEPATH: Assets/Scripts/Gate/ForceGate.cs
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

    [Tooltip("If true, painting reduces strength globally (anywhere on the trigger). If false, we weaken only near the painted point.")]
    [SerializeField] private bool globalWeakening = true;

    [Tooltip("Radius (meters) for local weakening around the paint hit when globalWeakening = false.")]
    [SerializeField] private float localWeakeningRadius = 1.0f;

    [Tooltip("Falloff sharpness for local weakening.")]
    [SerializeField] private float localFalloffSharpness = 3.0f;

    [Header("Physics")]
    [Tooltip("Continuous push mode. Force is applied every FixedUpdate while a body is inside.")]
    [SerializeField] private ForceMode forceMode = ForceMode.Force;

    [Tooltip("Multiply force by Rigidbody mass (useful when using Acceleration).")]
    [SerializeField] private bool scaleByMass = false;

    [Header("Debug")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(1, 0.4f, 0.2f, 0.15f);

    // ---- internal state ----
    private BoxCollider _col;
    private float _paintAccumMeters; // global accumulation

    // simple local map: we keep a fading center point & intensity
    private Vector3 _lastPaintPointWS;
    private float _localWeakAccum; // meters near last paint

    void Awake()
    {
        _col = GetComponent<BoxCollider>();
        _col.isTrigger = true; // we’re a force volume, not a solid collider

        if (metersToFullyOpen < 0.001f) metersToFullyOpen = 0.001f;
        if (baseOpposeForce < 0f) baseOpposeForce = 0f;
        if (minForceToStop < 0f) minForceToStop = 0f;
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
        }
        else
        {
            // keep a simple local influence blob around the last hit
            _lastPaintPointWS = worldPoint;
            _localWeakAccum = Mathf.Min(metersToFullyOpen, _localWeakAccum + metersDrawn);
        }
    }

    /// <summary>
    /// Current opposing force (0..base), reduced by painting.
    /// </summary>
    public float CurrentOpposeForce(Vector3 samplePointWS)
    {
        if (globalWeakening)
        {
            float t = Mathf.Clamp01(_paintAccumMeters / metersToFullyOpen);
            return Mathf.Max(0f, baseOpposeForce * (1f - t));
        }
        else
        {
            // Local weakening: gaussian falloff around last paint
            float dist = Vector3.Distance(samplePointWS, _lastPaintPointWS);
            float sigma2 = Mathf.Max(1e-5f, (localWeakeningRadius * localWeakeningRadius) / localFalloffSharpness);
            float w = Mathf.Exp(-(dist * dist) / (2f * sigma2)); // 0..1
            float tLocal = Mathf.Clamp01(_localWeakAccum / metersToFullyOpen);
            float localReduction = tLocal * w; // 0..1 fraction
            float force = baseOpposeForce * (1f - localReduction);
            return Mathf.Max(0f, force);
        }
    }

    private Vector3 BlockDirWS()
    {
        if (overrideBlockDirectionWS.sqrMagnitude > 1e-8f)
            return overrideBlockDirectionWS.normalized;
        return transform.forward; // default
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
            // Determine if the body is trying to go through (velocity projects forward)
            float vDot = Vector3.Dot(rb.velocity, pushDir);

            // Constant opposing force to resist entry. Optionally scale by mass.
            float forceToApply = opposeForce;
            if (scaleByMass && (forceMode == ForceMode.Force || forceMode == ForceMode.Acceleration))
                forceToApply *= rb.mass;

            // Always push opposite to gate forward, stronger if moving into it
            Vector3 forceWS = -pushDir * (forceToApply * Mathf.Max(0.5f, 1f + Mathf.Max(0f, vDot)));

            rb.AddForce(forceWS, forceMode);

            // Optional light position correction to avoid jitter inside the trigger
            // project the point out slightly opposite to forward
            rb.position += -pushDir * 0.0001f;
        }
        // else: too weak to stop → do nothing, passage allowed
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;
        var col = GetComponent<BoxCollider>();
        if (!col) return;

        Gizmos.color = gizmoColor;
        Matrix4x4 m = Matrix4x4.TRS(transform.TransformPoint(col.center), transform.rotation, Vector3.one);
        Gizmos.matrix = m;
        Gizmos.DrawCube(Vector3.zero, col.size);

        // draw arrow
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 p = transform.TransformPoint(col.center);
        Vector3 dir = (overrideBlockDirectionWS.sqrMagnitude > 1e-8f ? overrideBlockDirectionWS.normalized : transform.forward);
        Gizmos.color = new Color(1, 0.2f, 0.1f, 1f);
        Gizmos.DrawLine(p, p + dir * 0.75f);
        Gizmos.DrawSphere(p + dir * 0.75f, 0.03f);
    }
}
