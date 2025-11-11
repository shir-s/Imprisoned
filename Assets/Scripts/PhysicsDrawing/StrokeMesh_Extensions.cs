// FILEPATH: Assets/Scripts/Painting/StrokeMesh_Extensions.cs
using UnityEngine;

/// <summary>
/// tiny helper in case your StrokeMesh doesn't expose SetMaterial.
/// If your StrokeMesh already has SetMaterial or material property, you can delete this file.
/// </summary>
public static class StrokeMeshExtensions
{
    public static void SetMaterial(this StrokeMesh stroke, Material m)
    {
        // Try common patterns safely; no-ops if not found.
        var mr = stroke.GetComponent<MeshRenderer>();
        if (!mr) mr = stroke.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;

        var mf = stroke.GetComponent<MeshFilter>();
        if (!mf) mf = stroke.gameObject.AddComponent<MeshFilter>();
        // The StrokeMesh script you have likely manages the mesh itself.
    }
}