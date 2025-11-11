// FILEPATH: Assets/Scripts/PhysicsDrawing/IToolWear.cs
using UnityEngine;

public interface IToolWear
{
    // Apply wear at a world-space contact with given normal.
    // 'amount' is in meters removed (scaled by distance you dragged).
    void WearAt(Vector3 contactPointWorld, Vector3 surfaceNormalWorld, float amount, float radius);
}