// FILEPATH: Assets/Scripts/Painting/IMovementPainter.cs
using UnityEngine;

public interface IMovementPainter
{
    /// <summary>
    /// Called once when we start painting (first valid movement step).
    /// </summary>
    void OnMovementStart(Vector3 worldPos);

    /// <summary>
    /// Called for each movement step.
    /// from -> previous position, to -> current position.
    /// stepMeters = distance in meters.
    /// </summary>
    void OnMoveStep(Vector3 from, Vector3 to, float stepMeters, float deltaTime);

    /// <summary>
    /// Called when we stop painting (e.g. cube stopped or painting disabled).
    /// </summary>
    void OnMovementEnd(Vector3 worldPos);
}