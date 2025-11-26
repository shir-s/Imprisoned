// FILEPATH: Assets/Scripts/Painting/Shapes/StrokeHistory.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sample of the stroke. Stored in LOCAL space of the hit surface,
/// so it stays attached when the surface tilts/moves.
/// </summary>
public struct StrokeSample
{
    public Transform surface;      // collider.transform of the hit
    public Vector3   localPos;     // point in surface local space
    public Vector3   localNormal;  // normal in surface local space
    public float     time;         // Time.time when sampled

    public Vector3 WorldPos
    {
        get
        {
            if (surface == null) return localPos;
            return surface.TransformPoint(localPos);
        }
    }

    public Vector3 WorldNormal
    {
        get
        {
            if (surface == null) return localNormal.normalized;
            return surface.TransformDirection(localNormal).normalized;
        }
    }
}

/// <summary>
/// Sliding window of recent stroke samples + cumulative arc length (in meters).
/// </summary>
public class StrokeHistory
{
    private readonly List<StrokeSample> _samples   = new List<StrokeSample>();
    private readonly List<float>        _cumLength = new List<float>();   // cumulative arc length (meters)

    public int Count => _samples.Count;

    public StrokeSample this[int index] => _samples[index];

    /// <summary>Total length (meters) of the stored stroke segment.</summary>
    public float TotalLength => _cumLength.Count > 0 ? _cumLength[_cumLength.Count - 1] : 0f;

    /// <summary>Cumulative length at a given sample index.</summary>
    public float GetLengthAt(int index) => _cumLength[index];

    public void Clear()
    {
        _samples.Clear();
        _cumLength.Clear();
    }

    /// <summary>
    /// Append a new sample and update cumulative arc length (computed in WORLD space).
    /// </summary>
    public void AddSample(StrokeSample s)
    {
        float newLen = 0f;

        if (_samples.Count > 0)
        {
            Vector3 prev = _samples[_samples.Count - 1].WorldPos;
            Vector3 curr = s.WorldPos;
            newLen = _cumLength[_cumLength.Count - 1] + Vector3.Distance(prev, curr);
        }

        _samples.Add(s);
        _cumLength.Add(newLen);
    }

    /// <summary>
    /// New behaviour:
    /// - Ignore length for pruning (you can still pass it in, it's just unused).
    /// - Only when Count > maxHistoryPoints we delete the OLDEST 5% of samples.
    ///   This keeps a long "snake" and slowly scrolls it.
    /// </summary>
    public void Prune(float maxHistoryLength, int maxHistoryPoints)
    {
        if (_samples.Count == 0 || maxHistoryPoints <= 0)
            return;

        if (_samples.Count <= maxHistoryPoints)
            return;

        // delete 5% of current samples (at least 1, and leave at least 2 total)
        int removeCount = Mathf.Max(1, Mathf.FloorToInt(_samples.Count * 0.05f));
        if (_samples.Count - removeCount < 2)
            removeCount = _samples.Count - 2;

        if (removeCount <= 0)
            return;

        Debug.Log($"[StrokeHistory] PRUNE removing {removeCount} samples. " +
                  $"before: count={_samples.Count}, totalLen={TotalLength:F3}");

        _samples.RemoveRange(0, removeCount);
        _cumLength.RemoveRange(0, removeCount);

        // Rebase cumulative distances so first sample is at 0
        float offset = _cumLength[0];
        for (int i = 0; i < _cumLength.Count; i++)
            _cumLength[i] -= offset;
    }

    /// <summary>
    /// Remove samples [0..endIndexInclusive] – used when a detector consumes a shape.
    /// </summary>
    public void ConsumeUpTo(int endIndexInclusive)
    {
        int countRemove = endIndexInclusive + 1;
        if (countRemove <= 0 || countRemove > _samples.Count)
            return;

        _samples.RemoveRange(0, countRemove);
        _cumLength.RemoveRange(0, countRemove);

        if (_cumLength.Count > 0)
        {
            float offset = _cumLength[0];
            for (int i = 0; i < _cumLength.Count; i++)
                _cumLength[i] -= offset;
        }
    }
}
