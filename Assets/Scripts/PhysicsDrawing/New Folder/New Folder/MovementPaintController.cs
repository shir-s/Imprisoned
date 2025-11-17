using UnityEngine;

/// <summary>
/// Watches this object's movement and forwards it to one or more IMovementPainter strategies.
/// Supports sub-stepping: large movement in one frame is split into a limited number of
/// smaller segments so fast motion does not create gaps in the painted trail,
/// while capping the number of steps per frame to avoid lag.
/// </summary>
[DisallowMultipleComponent]
public class MovementPaintController : MonoBehaviour
{
    [Header("Movement Sampling")]
    [Tooltip("Ignore movement smaller than this (meters) before sending ANY painting steps (anti-jitter).")]
    [SerializeField] private float minStepDistance = 0.001f;

    [Tooltip("Maximum length (meters) of a single painter step. Larger frame movement will be subdivided.")]
    [SerializeField] private float maxSegmentLength = 0.002f;

    [Tooltip("Maximum number of sub-steps we will send per frame (performance safety).")]
    [SerializeField] private int maxSegmentsPerFrame = 24;

    [Tooltip("If true, send movement to ALL painters on this object. If false, only the active index.")]
    [SerializeField] private bool useAllPainters = false;

    [Tooltip("Index into the found IMovementPainter components (0 = first).")]
    [SerializeField] private int activePainterIndex = 0;

    [Header("Debug")]
    [SerializeField] private bool logSteps = false;

    private Vector3 _prevPos;
    private bool _hasPrev;
    private IMovementPainter[] _painters;
    private bool _wasPainting;
    public System.Action<float, bool> OnPaintingUpdate;

    private void Awake()
    {
        // Auto-discover all IMovementPainter components on this GameObject
        _painters = GetComponents<IMovementPainter>();
        if (logSteps)
            Debug.Log($"[MovementPaintController] Found {_painters.Length} painters on {name}");
    }

    private void OnEnable()
    {
        _hasPrev = false;
        _wasPainting = false;
    }

    private void OnDisable()
    {
        StopPaintingIfNeeded();
    }

    private void OnValidate()
    {
        if (minStepDistance < 0f)       minStepDistance = 0f;
        if (maxSegmentLength <= 0f)     maxSegmentLength = 0.0005f;
        if (maxSegmentsPerFrame < 1)    maxSegmentsPerFrame = 1;
    }

    private void LateUpdate()
    {
        if (_painters == null || _painters.Length == 0)
            return;

        Vector3 pos = transform.position;

        // First frame: just initialize
        if (!_hasPrev)
        {
            _prevPos = pos;
            _hasPrev = true;
            OnPaintingUpdate?.Invoke(0f, false);
            return;
        }

        Vector3 delta = pos - _prevPos;
        float dist = delta.magnitude;

        if (!float.IsFinite(dist))
        {
            _prevPos = pos;
            OnPaintingUpdate?.Invoke(0f, false);
            return;
        }

        // Barely moved: possibly stop painting
        if (dist < minStepDistance)
        {
            if (_wasPainting)
                StopPaintingIfNeeded();

            OnPaintingUpdate?.Invoke(0f, false);
            _prevPos = pos;
            return;
        }

        // We have real movement
        if (!_wasPainting)
        {
            StartPainting(_prevPos);
            _wasPainting = true;
        }

        SubdivideAndPaint(_prevPos, pos, dist, Time.deltaTime);

        if (logSteps)
            Debug.Log($"[MovementPaintController] Frame movement dist={dist:F4} on {name}");

        float speed = dist / Mathf.Max(Time.deltaTime, 0.00001f);
        OnPaintingUpdate?.Invoke(speed, true);
        _prevPos = pos;
    }

    // --- Painter calls ---

    private void StartPainting(Vector3 worldPos)
    {
        if (useAllPainters)
        {
            foreach (var p in _painters)
                if (p != null)
                    p.OnMovementStart(worldPos);
        }
        else
        {
            var p = GetActivePainter();
            if (p != null)
                p.OnMovementStart(worldPos);
        }
    }

    /// <summary>
    /// Break a long movement into smaller segments (clamped by maxSegmentsPerFrame)
    /// and send each segment to the painters.
    /// </summary>
    private void SubdivideAndPaint(Vector3 from, Vector3 to, float dist, float dt)
    {
        if (dist <= 0f || !float.IsFinite(dist))
            return;

        float segLen = Mathf.Max(0.000001f, maxSegmentLength);

        int steps = Mathf.CeilToInt(dist / segLen);
        if (steps < 1) steps = 1;
        if (steps > maxSegmentsPerFrame) steps = maxSegmentsPerFrame;

        float invSteps = 1f / steps;
        Vector3 dir = to - from;

        for (int i = 0; i < steps; i++)
        {
            float t0 = i * invSteps;
            float t1 = (i + 1) * invSteps;

            Vector3 p0 = from + dir * t0;
            Vector3 p1 = from + dir * t1;

            float segDist = Vector3.Distance(p0, p1);
            if (segDist <= 0f || !float.IsFinite(segDist))
                continue;

            float segDt = dt * invSteps;
            CallPaintersOnStep(p0, p1, segDist, segDt);
        }
    }

    private void CallPaintersOnStep(Vector3 from, Vector3 to, float stepMeters, float dt)
    {
        if (useAllPainters)
        {
            foreach (var p in _painters)
                if (p != null)
                    p.OnMoveStep(from, to, stepMeters, dt);
        }
        else
        {
            var p = GetActivePainter();
            if (p != null)
                p.OnMoveStep(from, to, stepMeters, dt);
        }
    }

    private void StopPaintingIfNeeded()
    {
        if (!_wasPainting) return;
        _wasPainting = false;

        Vector3 pos = transform.position;

        if (useAllPainters)
        {
            foreach (var p in _painters)
                if (p != null)
                    p.OnMovementEnd(pos);
        }
        else
        {
            var p = GetActivePainter();
            if (p != null)
                p.OnMovementEnd(pos);
        }
    }

    private IMovementPainter GetActivePainter()
    {
        if (_painters == null || _painters.Length == 0)
            return null;

        if (activePainterIndex < 0 || activePainterIndex >= _painters.Length)
            return null;

        return _painters[activePainterIndex];
    }

    // Optional: allow switching painters at runtime (eg. via key or UI)
    public void SetActivePainter(int index)
    {
        activePainterIndex = index;
    }
}
