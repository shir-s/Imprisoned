// FILEPATH: Assets/Scripts/Painting/StrokeWidthBinder.cs
using UnityEngine;

/// <summary>
/// Pushes the brush cube's face width (in meters) into the stroke material,
/// so the shader locks visible width to match the cube.
/// Attach this to the stroke GameObject (has a Renderer).
/// Provide a reference to the cube (brush) Transform.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class StrokeWidthBinder : MonoBehaviour
{
    [SerializeField] private Transform brushCube;     // the cube you use to draw
    [SerializeField] private string widthProp = "_DesiredWorldWidth";
    [SerializeField] private Vector3 cubeFaceNormal = Vector3.up; // which face paints? (normal points out of that face)

    [SerializeField, Range(0f, 1f)] private float coreFill = 1f; // 1 = fill full target width
    [SerializeField, Range(0f, 0.5f)] private float edgeFeather = 0.10f;

    Renderer _r;
    MaterialPropertyBlock _mpb;

    void Awake()
    {
        _r = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    void LateUpdate()
    {
        if (!brushCube) return;

        // Determine the painting face size on the cube in world meters.
        // Pick the two in-plane axes orthogonal to cubeFaceNormal.
        Vector3 n = cubeFaceNormal.normalized;
        Vector3 xAxis = Vector3.Cross(n, Vector3.up);
        if (xAxis.sqrMagnitude < 1e-6f) xAxis = Vector3.Cross(n, Vector3.right);
        xAxis.Normalize();
        Vector3 yAxis = Vector3.Cross(n, xAxis).normalized;

        // Project cube lossyScale onto those axes to get edge lengths.
        Vector3 s = brushCube.lossyScale;
        // Assume a unit cube mesh originally (-0.5..0.5): edge lengths == scales along local axes.
        // The face width we want to match is the length along the *stroke width* direction.
        // If your stroke runs forward from the face, use the *shorter* in-plane dimension as "width".
        float a = Vector3.Scale(brushCube.right,  s).magnitude * Mathf.Abs(Vector3.Dot(brushCube.right,  xAxis));
        float b = Vector3.Scale(brushCube.up,     s).magnitude * Mathf.Abs(Vector3.Dot(brushCube.up,     xAxis));
        float c = Vector3.Scale(brushCube.forward,s).magnitude * Mathf.Abs(Vector3.Dot(brushCube.forward, xAxis));
        float widthX = Mathf.Max(a, Mathf.Max(b, c)); // project max onto xAxis

        // Fetch current material block, set values, and apply.
        _r.GetPropertyBlock(_mpb);
        _mpb.SetFloat(widthProp, widthX);              // meters
        _mpb.SetFloat("_CoreFill", coreFill);
        _mpb.SetFloat("_EdgeSoft", edgeFeather);
        _r.SetPropertyBlock(_mpb);
    }
}
