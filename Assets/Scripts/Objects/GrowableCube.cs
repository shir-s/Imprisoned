// FILEPATH: Assets/Scripts/Growth/GrowableCube.cs
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class GrowableCube : MonoBehaviour
{
    [Header("Cube Growth Settings")]
    [SerializeField] private Material cubeMaterial;
    [SerializeField] private float shrinkSpeed = 0.1f;       // meters of side shrink per meter drawn
    [SerializeField] private float minSideScale = 0.4f;      // spawn when any side ≤ base * this
    [SerializeField] private float restoreSpeed = 1.0f;      // how fast the parent returns to full size
    [SerializeField] private float cubeSize = 1f;            // base size (meters)
    [SerializeField] private float gapBetweenCubes = 0.02f;  // spacing between cube faces

    [Header("Layer Settings")]
    [Tooltip("If -1, inherits parent's layer; otherwise uses this fixed layer for spawned cubes.")]
    [SerializeField] private int cubeLayer = -1;

    [Header("Attachment Settings")]
    [Tooltip("When true, child cubes become children of the cube they grow from (keeps them physically attached).")]
    [SerializeField] private bool attachChildren = true;

    [Header("Debug")]
    [SerializeField] private bool showDebug = false;

    private Vector3 _baseScale;
    private bool _isRestoring;
    private float _restoreT;
    private Vector3 _restoreTarget;
    private bool _hasSpawnedChild;
    private Transform _childCube;
    private Vector3 _spawnNormalWS;

    void Awake()
    {
        // ensure a valid scale
        if (Mathf.Approximately(transform.localScale.x, 0f) ||
            Mathf.Approximately(transform.localScale.y, 0f) ||
            Mathf.Approximately(transform.localScale.z, 0f))
        {
            transform.localScale = Vector3.one * cubeSize;
        }

        _baseScale = transform.localScale;

        var col = GetComponent<BoxCollider>();
        col.isTrigger = false;

        var mr = GetComponent<Renderer>();
        if (mr && cubeMaterial) mr.sharedMaterial = cubeMaterial;

        // Default cube layer if not manually set
        if (cubeLayer == -1)
            cubeLayer = gameObject.layer;
    }

    /// <summary>
    /// Called by MouseBrushPainter when the user draws on this cube.
    /// worldNormal: the hit normal from the raycast
    /// metersDrawn: path length since last sample (meters)
    /// </summary>
    public void OnDrawOnFace(Vector3 worldNormal, float metersDrawn)
    {
        if (metersDrawn <= 0f) return;
        if (_isRestoring) return;

        // convert normal to local space
        Vector3 localNormal = transform.InverseTransformDirection(worldNormal);
        Vector3 shrinkAxis = DominantAxis(localNormal); // ±X, ±Y, or ±Z

        Vector3 s = transform.localScale;
        float delta = shrinkSpeed * metersDrawn;

        if (Mathf.Abs(shrinkAxis.x) > 0f) s.x = Mathf.Max(_baseScale.x * minSideScale, s.x - delta);
        if (Mathf.Abs(shrinkAxis.y) > 0f) s.y = Mathf.Max(_baseScale.y * minSideScale, s.y - delta);
        if (Mathf.Abs(shrinkAxis.z) > 0f) s.z = Mathf.Max(_baseScale.z * minSideScale, s.z - delta);

        transform.localScale = s;
        _spawnNormalWS = transform.TransformDirection(shrinkAxis);

        // spawn condition
        if (!_hasSpawnedChild &&
            (s.x <= _baseScale.x * minSideScale ||
             s.y <= _baseScale.y * minSideScale ||
             s.z <= _baseScale.z * minSideScale))
        {
            SpawnChildCube();
        }
    }

    private static Vector3 DominantAxis(Vector3 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);
        float az = Mathf.Abs(v.z);
        if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0, 0);
        if (ay >= az)             return new Vector3(0, Mathf.Sign(v.y), 0);
        return new Vector3(0, 0, Mathf.Sign(v.z));
    }

    private void SpawnChildCube()
    {
        _hasSpawnedChild = true;

        // begin restoring parent
        _isRestoring = true;
        _restoreT = 0f;
        _restoreTarget = _baseScale;

        float half = cubeSize * 0.5f;
        Vector3 offset = _spawnNormalWS.normalized * (half + gapBetweenCubes + half);
        Vector3 spawnPos = transform.position + offset;

        // create child cube
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = "GrowCube_Child";
        child.transform.localScale = Vector3.one * cubeSize;
        child.transform.position = spawnPos;
        child.transform.rotation = transform.rotation;

        // layer
        child.layer = cubeLayer;

        // material
        var mr = child.GetComponent<Renderer>();
        if (mr && cubeMaterial) mr.sharedMaterial = cubeMaterial;

        // attach as physical child of this cube
        if (attachChildren)
            child.transform.SetParent(transform, true);
        else
            child.transform.SetParent(transform.parent, true);

        // copy behaviour settings
        var childComp = child.AddComponent<GrowableCube>();
        childComp.cubeMaterial    = cubeMaterial;
        childComp.shrinkSpeed     = shrinkSpeed;
        childComp.minSideScale    = minSideScale;
        childComp.restoreSpeed    = restoreSpeed;
        childComp.cubeSize        = cubeSize;
        childComp.gapBetweenCubes = gapBetweenCubes;
        childComp.cubeLayer       = cubeLayer;
        childComp.attachChildren  = attachChildren;
        childComp.showDebug       = showDebug;

        _childCube = child.transform;
    }

    void Update()
    {
        if (_isRestoring)
        {
            _restoreT += Time.deltaTime * Mathf.Max(0.01f, restoreSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, _restoreTarget, _restoreT);
            if (_restoreT >= 1f - 1e-4f)
            {
                transform.localScale = _restoreTarget;
                _isRestoring = false;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebug) return;
        Gizmos.color = Color.green;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
