// FILEPATH: Assets/Scripts/Painting/Trails/StrokeCrossingDetector.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data payload sent with EventManager.GameEvent.StrokeCrossingDetected.
/// </summary>
public struct StrokeCrossingEventData
{
    public StrokeTrailRecorder recorder;     // which recorder / cube
    public int newSampleIndex;              // index of the *new* sample
    public int otherSampleIndex;            // closest older sample index
    public StrokeTurnCategory category;     // angle category at the crossing
}

[DisallowMultipleComponent]
public class StrokeCrossingDetector : MonoBehaviour
{
    [SerializeField] private StrokeTrailRecorder recorder;

    [Header("Crossing Detection")]
    [Tooltip("World-space radius within which two points count as a crossing candidate.")]
    [SerializeField] private float crossingRadius = 0.8f;

    [Tooltip("Minimum index difference between the two samples.\n" +
             "If they are closer than this in index space, it's treated as 'just neighbours', not a crossing.")]
    [SerializeField] private int minIndexSeparation = 3;

    [Tooltip("How many samples before/after each point to use to estimate local direction.")]
    [SerializeField] private int directionWindow = 2;

    [Tooltip("Angle (deg) below which the two stroke directions are considered 'parallel',\n" +
             "so the situation is treated as 'two lines close but not crossing'.")]
    [SerializeField] private float parallelAngleMax = 15f;

    [Header("Debug")]
    [SerializeField] private bool debugCrossings = true;

    // For visualization / gameplay: category per sample index (for the *new* index)
    private readonly Dictionary<int, StrokeTurnCategory> _crossingByIndex = new();

    private StrokeHistory History => recorder ? recorder.History : null;

    private void LateUpdate()
    {
        if (!recorder) return;
        DetectNewCrossingForLastPoint();
    }

    /// <summary>
    /// Visualizer / analyzer can ask: "is this index a crossing and what type?"
    /// (Still useful for debug even though we now also fire an event.)
    /// </summary>
    public bool TryGetCrossingCategoryAt(int index, out StrokeTurnCategory category)
    {
        return _crossingByIndex.TryGetValue(index, out category);
    }

    public void ClearAllCrossings()
    {
        _crossingByIndex.Clear();
    }

    private void DetectNewCrossingForLastPoint()
    {
        var history = History;
        if (history == null) return;

        int count = history.Count;
        if (count < 4) return;

        int newIdx = count - 1;
        TryDetectCrossingAt(newIdx);
    }

    private void TryDetectCrossingAt(int newIdx)
    {
        var history = History;
        if (history == null) return;

        Vector3 newPos = history[newIdx].WorldPos;

        int   bestOldIdx = -1;
        float bestDist   = float.PositiveInfinity;

        // 1) Find the closest earlier point in WORLD space
        for (int i = 0; i < newIdx - 1; i++)
        {
            float d = Vector3.Distance(newPos, history[i].WorldPos);
            if (d < bestDist)
            {
                bestDist   = d;
                bestOldIdx = i;
            }
        }

        // Not close enough in space → no candidate
        if (bestOldIdx < 0 || bestDist > crossingRadius)
            return;

        // 2) Index separation test – avoid treating local turns as crossings
        int indexDiff = Mathf.Abs(newIdx - bestOldIdx);
        if (indexDiff < minIndexSeparation)
            return;

        // 3) Build directions around each point (using several samples)
        if (!TryGetDirectionAround(history, bestOldIdx, directionWindow, out Vector3 dirA) ||
            !TryGetDirectionAround(history, newIdx,    directionWindow, out Vector3 dirB))
        {
            return; // couldn't get stable directions
        }

        float angleDeg = Vector3.Angle(dirA, dirB);

        // 4) If directions are almost parallel → "two lines close but not crossing"
        if (angleDeg < parallelAngleMax)
            return;

        // 5) Classify the angle into Small / Medium / Sharp using the same thresholds
        StrokeTurnCategory cat = ClassifyAngle(angleDeg);
        if (cat == StrokeTurnCategory.None)
            return;

        if (debugCrossings)
        {
            Debug.Log(
                $"[StrokeCrossingDetector] Crossing newIdx={newIdx}, oldIdx={bestOldIdx}, " +
                $"dist={bestDist:F3}, indexDiff={indexDiff}, angle={angleDeg:F1}° ({cat})");

            Debug.DrawLine(newPos, history[bestOldIdx].WorldPos, Color.cyan, 1f);
        }

        // Tag the *new* sample index as a crossing of this category
        _crossingByIndex[newIdx] = cat;

        // Fire global event so analyzers can react immediately
        var payload = new StrokeCrossingEventData
        {
            recorder        = recorder,
            newSampleIndex  = newIdx,
            otherSampleIndex= bestOldIdx,
            category        = cat
        };

        EventManager.TriggerEvent(EventManager.GameEvent.StrokeCrossingDetected, payload);
    }

    /// <summary>
    /// Compute a direction vector at a given index by looking 'window' samples
    /// before and after it and subtracting.
    /// </summary>
    private bool TryGetDirectionAround(StrokeHistory history, int centerIndex, int window, out Vector3 dir)
    {
        int count = history.Count;
        dir = Vector3.zero;

        if (count < 2) return false;
        if (window < 1) window = 1;

        int idxA = Mathf.Max(0, centerIndex - window);
        int idxB = Mathf.Min(count - 1, centerIndex + window);

        if (idxB <= idxA)
            return false;

        Vector3 a = history[idxA].WorldPos;
        Vector3 b = history[idxB].WorldPos;

        dir = b - a;
        float magSq = dir.sqrMagnitude;
        if (magSq < 1e-6f)
            return false;

        dir /= Mathf.Sqrt(magSq);
        return true;
    }

    /// <summary>
    /// Same thresholds as StrokeTurnUtils, just re-used here for crossings.
    /// </summary>
    private StrokeTurnCategory ClassifyAngle(float angleDeg)
    {
        float a = Mathf.Abs(angleDeg);

        if (a < StrokeTurnUtils.SmallTurnMax)      return StrokeTurnCategory.Small;
        if (a < StrokeTurnUtils.MediumTurnMax)     return StrokeTurnCategory.Medium;
        if (a >= StrokeTurnUtils.SharpTurnMin)     return StrokeTurnCategory.Sharp;
        return StrokeTurnCategory.None;
    }

    private void OnValidate()
    {
        if (crossingRadius < 0f) crossingRadius = 0f;
        if (minIndexSeparation < 1) minIndexSeparation = 1;
        if (directionWindow < 1) directionWindow = 1;
        if (parallelAngleMax < 0f) parallelAngleMax = 0f;
    }
}
