// FILEPATH: Assets/Scripts/Environment/RiverZone.cs
using System;
using JellyGame.GamePlay.Painting.Trails.Collision;
using JellyGame.GamePlay.Player;
using UnityEngine;

/// <summary>
/// River / slow zone:
/// - Has a trigger collider.
/// - When something enters / stays inside, it:
///     * Optionally slows units based on tag -> speed multiplier.
///     * Optionally disables StrokeTrailRecorder while in water,
///       so no new trail points appear in the river.
/// - If the unit is standing on a sunk TraySurfaceStickRigidbody (bridge block),
///   all river penalties are ignored: no slow and trail recording stays enabled.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class RiverZone : MonoBehaviour
{
    [Serializable]
    private class TagSpeedModifier
    {
        [Tooltip("Tag of the object that will be slowed in this river.")]
        public string tag = "Player";

        [Tooltip("Multiplier applied to that object's movement speed. 1 = no change, 0.5 = half speed.")]
        public float speedMultiplier = 0.5f;
    }

    [Header("Speed reduction")]
    [Tooltip("Per-tag speed multipliers. Objects with these tags will be slowed when inside the river.")]
    [SerializeField] private TagSpeedModifier[] tagSpeedModifiers;

    [Header("Stroke recording")]
    [Tooltip("If true, any object with a StrokeTrailRecorder will stop recording new points while in the river (unless on a bridge).")]
    [SerializeField] private bool disableStrokeRecordingInRiver = true;

    [Header("Bridge logic")]
    [Tooltip("If true, units standing on a sunk TraySurfaceStickRigidbody will NOT be slowed or have their strokes disabled.")]
    [SerializeField] private bool respectBridges = true;

    [Tooltip("Max distance for the raycast used to detect sunk bridge blocks below the unit.")]
    [SerializeField] private float bridgeCheckDistance = 1.0f;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col)
            col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        UpdateEffectsForObject(other);
    }

    private void OnTriggerStay(Collider other)
    {
        UpdateEffectsForObject(other);
    }

    private void OnTriggerExit(Collider other)
    {
        // Leaving the river → always clear modifiers & re-enable recording.
        RemoveSpeedModifier(other);

        if (disableStrokeRecordingInRiver)
            SetStrokeRecorderEnabled(other, true);
    }

    // -------------------------------------------------------
    // Core river logic per object
    // -------------------------------------------------------

    private void UpdateEffectsForObject(Collider other)
    {
        bool onBridge = respectBridges && IsOnBridge(other);

        if (onBridge)
        {
            // On a sunk bridge block: no slow, trail allowed.
            RemoveSpeedModifier(other);

            if (disableStrokeRecordingInRiver)
                SetStrokeRecorderEnabled(other, true);
        }
        else
        {
            // In water with no bridge under feet.
            ApplySpeedModifier(other);

            if (disableStrokeRecordingInRiver)
                SetStrokeRecorderEnabled(other, false);
        }
    }

    // -------------------------------------------------------
    // Speed handling
    // -------------------------------------------------------

    private void ApplySpeedModifier(Collider other)
    {
        if (!TryGetMultiplierForTag(other.tag, out float multiplier))
            return;

        var rider = other.GetComponentInParent<KinematicTrayRider>();
        if (rider != null)
        {
            rider.SetSpeedMultiplier(multiplier);
        }
    }

    private void RemoveSpeedModifier(Collider other)
    {
        if (!TryGetMultiplierForTag(other.tag, out _))
            return;

        var rider = other.GetComponentInParent<KinematicTrayRider>();
        if (rider != null)
        {
            rider.ClearSpeedMultiplier();
        }
    }

    private bool TryGetMultiplierForTag(string objectTag, out float multiplier)
    {
        multiplier = 1f;

        if (tagSpeedModifiers == null)
            return false;

        for (int i = 0; i < tagSpeedModifiers.Length; i++)
        {
            var entry = tagSpeedModifiers[i];
            if (!string.IsNullOrEmpty(entry.tag) && entry.tag == objectTag)
            {
                multiplier = Mathf.Max(0f, entry.speedMultiplier);
                return true;
            }
        }

        return false;
    }

    // -------------------------------------------------------
    // Stroke recorder handling
    // -------------------------------------------------------

    private void SetStrokeRecorderEnabled(Collider other, bool enabled)
    {
        var recorder = other.GetComponentInParent<StrokeTrailRecorder>();
        if (recorder != null)
        {
            recorder.RecordingEnabled = enabled;
        }
    }

    // -------------------------------------------------------
    // Bridge detection
    // -------------------------------------------------------

    /// <summary>
    /// Returns true if there is a sunk TraySurfaceStickRigidbody (bridge block)
    /// directly under this actor within bridgeCheckDistance.
    /// </summary>
    private bool IsOnBridge(Collider actor)
    {
        if (!respectBridges)
            return false;

        // Raycast downward from actor's center.
        // If your world "up" is tray.up you can swap Vector3.down with -tray.up,
        // but for now world-down is good enough.
        Vector3 origin = actor.bounds.center + Vector3.up * 0.1f;
        Vector3 dir = Vector3.down;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, bridgeCheckDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            // Look for a sunk TraySurfaceStickRigidbody under the actor.
            var stick = hit.collider.GetComponentInParent<TraySurfaceStickRigidbody>();
            if (stick != null && stick.IsSunk)
            {
                return true;
            }
        }

        return false;
    }
}
