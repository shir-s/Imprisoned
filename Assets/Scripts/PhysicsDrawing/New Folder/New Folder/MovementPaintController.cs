// FILEPATH: Assets/Scripts/Painting/MovementPaintController.cs
using UnityEngine;

/// <summary>
/// Watches this object's movement and forwards it to one or more IMovementPainter strategies.
/// This lets you swap painting methods without touching the movement scripts.
/// </summary>
[DisallowMultipleComponent]
public class MovementPaintController : MonoBehaviour
{
    [Header("Movement Sampling")]
    [Tooltip("Ignore movement smaller than this (meters) before sending painting steps.")]
    [SerializeField] private float minStepDistance = 0.001f;

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

    void Awake()
    {
        // Auto-discover all IMovementPainter components on this GameObject
        _painters = GetComponents<IMovementPainter>();
        if (logSteps)
            Debug.Log($"[MovementPaintController] Found {_painters.Length} painters on {name}");
    }

    void OnEnable()
    {
        _hasPrev = false;
        _wasPainting = false;
    }

    void OnDisable()
    {
        StopPaintingIfNeeded();
    }

    void LateUpdate()
    {
        if (_painters == null || _painters.Length == 0)
            return;

        Vector3 pos = transform.position;

        if (!_hasPrev)
        {
            _prevPos = pos;
            _hasPrev = true;
            return;
        }

        float dist = (pos - _prevPos).magnitude;
        if (!float.IsFinite(dist))
        {
            _prevPos = pos;
            return;
        }

        if (dist < minStepDistance)
        {
            // If we were painting and now basically stopped, end painting once
            if (_wasPainting)
                StopPaintingIfNeeded();
            _prevPos = pos;
            return;
        }

        // We have a valid movement step
        if (!_wasPainting)
        {
            StartPainting(pos);
            _wasPainting = true;
        }

        CallPaintersOnStep(_prevPos, pos, dist, Time.deltaTime);

        if (logSteps)
            Debug.Log($"[MovementPaintController] Step dist={dist:F4} on {name}");

        _prevPos = pos;
    }

    // --- painter calls ---

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

        var pos = transform.position;

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
