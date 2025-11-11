// FILEPATH: Assets/Scripts/Gate/GateGrid.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GateGrid : MonoBehaviour
{
    public enum GrowthMode { OnlyPaintedCells, AllCellsTogether }
    public enum SpreadShape { Circle, AxisDominant, Square, GridUniform }

    [Header("Grid Size")]
    [SerializeField] private int rows = 100;
    [SerializeField] private int cols = 100;

    [Header("Cell Geometry")]
    [SerializeField] private Vector2 cellSize = new Vector2(0.08f, 0.08f); // X by Y (local)
    [SerializeField] private float depth = 0.08f;                          // Z thickness
    [SerializeField] private Vector2 baseGap = new Vector2(0.01f, 0.01f);  // initial spacing

    [Header("Layers & Rendering")]
    [Tooltip("Layer index (0..31) for the small cubes (exclude this from brush layermask).")]
    [SerializeField, Range(0,31)] private int cellLayer = 0;
    [Tooltip("Layer index (0..31) for the big paint surface (include this in brush layermask).")]
    [SerializeField, Range(0,31)] private int paintSurfaceLayer = 0;
    [SerializeField] private bool centerAtOrigin = true;
    [SerializeField] private Material cellMaterial;

    [Header("Paint → Expansion")]
    [SerializeField] private GrowthMode growthMode = GrowthMode.OnlyPaintedCells;
    [SerializeField] private SpreadShape spreadShape = SpreadShape.Circle;

    [Tooltip("Meters pushed outward per meter painted (adds to SPREAD TARGET).")]
    [SerializeField] private float spreadPerMeter = 0.02f;

    [Tooltip("Scale change (unitless) added per meter painted (adds to SCALE TARGET).")]
    [SerializeField] private float scalePerMeter  = 0.25f;

    [SerializeField] private float falloffSharpness = 3.0f;  // (OnlyPaintedCells) gaussian-like falloff
    [SerializeField] private float maxScaleMul = 3.0f;       // clamp (applied to current scale)
    [SerializeField] private float maxSpread   = 4.0f;       // clamp (meters)

    [Header("GridUniform (Gap-Widening)")]
    [SerializeField] private Vector2 gapPerMeter = new Vector2(0.20f, 0.20f);
    [SerializeField] private Vector2 maxGapAbs = new Vector2(1.0f, 1.0f);

    [Header("Anti-Pop")]
    [SerializeField] private float maxMetersPerSample = 0.15f;
    [SerializeField] private float smoothingSpeed = 8f;

    [Header("Debug")]
    [SerializeField] private bool gizmosCenter = true;

    // Internal
    private Transform[,] _cells;

    // per-cell CURRENT values
    private Vector2[,] _spreadXY;
    private float[,]   _scaleMul;

    // per-cell TARGET values
    private Vector2[,] _spreadXY_T;
    private float[,]   _scaleMul_T;

    // Global CURRENT/TARGET (AllCellsTogether)
    private float _globalSpread;
    private float _globalScaleMul = 1f;
    private float _globalSpread_T;
    private float _globalScaleMul_T = 1f;

    // GridUniform CURRENT/TARGET
    private Vector2 _gapCurrent;
    private Vector2 _gapTarget;
    private float   _gridUniformScaleMul = 1f;
    private float   _gridUniformScaleMul_T = 1f;

    private Vector3[,] _baseLocalPos;
    private Vector3 _centerLocal;

    // ======= Big Paint Surface (non-colliding trigger with PaintSurfaceMarker) =======
    private GameObject _paintSurfaceGO;
    private BoxCollider _paintCol;
    private PaintSurfaceMarker _paintMarker;

    public int CellLayer  => Mathf.Clamp(cellLayer, 0, 31);
    public int PaintLayer => Mathf.Clamp(paintSurfaceLayer, 0, 31);

    void Awake() { Build(); }

    void Update()
    {
        float step = Mathf.Max(0f, smoothingSpeed) * Time.deltaTime;
        if (step <= 0f || _cells == null) { UpdatePaintSurfaceToBounds(_ComputeStaticBounds()); return; }

        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool boundsInit = false;

        if (spreadShape == SpreadShape.GridUniform)
        {
            _gapCurrent.x = Mathf.MoveTowards(_gapCurrent.x, _gapTarget.x, step * Mathf.Abs(_gapTarget.x - _gapCurrent.x) + 1e-6f);
            _gapCurrent.y = Mathf.MoveTowards(_gapCurrent.y, _gapTarget.y, step * Mathf.Abs(_gapTarget.y - _gapCurrent.y) + 1e-6f);
            _gapCurrent.x = Mathf.Clamp(_gapCurrent.x, baseGap.x, maxGapAbs.x);
            _gapCurrent.y = Mathf.Clamp(_gapCurrent.y, baseGap.y, maxGapAbs.y);

            _gridUniformScaleMul = Mathf.MoveTowards(_gridUniformScaleMul, _gridUniformScaleMul_T, step);
            _gridUniformScaleMul = Mathf.Clamp(_gridUniformScaleMul, 1f, maxScaleMul);

            float pitchX = cellSize.x + _gapCurrent.x;
            float pitchY = cellSize.y + _gapCurrent.y;
            float totalX = cols * pitchX - _gapCurrent.x;
            float totalY = rows * pitchY - _gapCurrent.y;
            Vector3 centerNow = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;

            Vector3 scaled = new Vector3(cellSize.x * _gridUniformScaleMul, cellSize.y * _gridUniformScaleMul, depth * _gridUniformScaleMul);

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float lx = c * pitchX + cellSize.x * 0.5f;
                float ly = r * pitchY + cellSize.y * 0.5f;
                Vector3 local = new Vector3(lx, ly, 0f);
                if (centerAtOrigin) local -= centerNow;

                var t = _cells[r, c];
                t.localPosition = local;
                t.localScale = scaled;
                t.gameObject.layer = CellLayer;

                Bounds b = new Bounds(local, scaled);
                if (!boundsInit) { localBounds = b; boundsInit = true; }
                else localBounds.Encapsulate(b);
            }

            UpdatePaintSurfaceToBounds(localBounds);
            return;
        }

        if (growthMode == GrowthMode.AllCellsTogether)
        {
            _globalSpread   = Mathf.MoveTowards(_globalSpread,   _globalSpread_T,   step);
            _globalSpread   = Mathf.Clamp(_globalSpread, 0f, maxSpread);
            _globalScaleMul = Mathf.MoveTowards(_globalScaleMul, _globalScaleMul_T, step);
            _globalScaleMul = Mathf.Clamp(_globalScaleMul, 1f, maxScaleMul);

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                Vector3 basePos = _baseLocalPos[r, c];

                Vector2 fromCenter = centerAtOrigin
                    ? new Vector2(basePos.x, basePos.y)
                    : new Vector2(basePos.x - _centerLocal.x, basePos.y - _centerLocal.y);

                Vector2 dir = OutwardDir(fromCenter);
                Vector3 pos = basePos + new Vector3(dir.x, dir.y, 0f) * _globalSpread;

                var t = _cells[r, c];
                t.localPosition = pos;
                t.localScale = new Vector3(
                    cellSize.x * _globalScaleMul,
                    cellSize.y * _globalScaleMul,
                    depth      * _globalScaleMul
                );
                t.gameObject.layer = CellLayer;

                Bounds b = new Bounds(pos, t.localScale);
                if (!boundsInit) { localBounds = b; boundsInit = true; }
                else localBounds.Encapsulate(b);
            }

            UpdatePaintSurfaceToBounds(localBounds);
        }
        else // OnlyPaintedCells
        {
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                Vector2 curS = _spreadXY[r, c];
                Vector2 tgtS = _spreadXY_T[r, c];
                Vector2 delta = tgtS - curS;
                float d = delta.magnitude;
                if (d > 0f)
                {
                    float move = Mathf.Min(d, step * (1f + d));
                    _spreadXY[r, c] = curS + delta.normalized * move;
                    if (_spreadXY[r, c].magnitude > maxSpread)
                        _spreadXY[r, c] = _spreadXY[r, c].normalized * maxSpread;
                }

                _scaleMul[r, c] = Mathf.MoveTowards(_scaleMul[r, c], _scaleMul_T[r, c], step);
                _scaleMul[r, c] = Mathf.Clamp(_scaleMul[r, c], 1f, maxScaleMul);

                Vector3 pos = _baseLocalPos[r, c] + new Vector3(_spreadXY[r, c].x, _spreadXY[r, c].y, 0f);
                var t = _cells[r, c];
                t.localPosition = pos;

                float mul = _scaleMul[r, c];
                Vector3 sca = new Vector3(cellSize.x * mul, cellSize.y * mul, depth * mul);
                t.localScale = sca;
                t.gameObject.layer = CellLayer;

                Bounds b = new Bounds(pos, sca);
                if (!boundsInit) { localBounds = b; boundsInit = true; }
                else localBounds.Encapsulate(b);
            }

            UpdatePaintSurfaceToBounds(localBounds);
        }
    }

    // Build grid of cubes + create the big paint surface trigger
    public void Build()
    {
        CleanupChildren();

        _cells        = new Transform[rows, cols];
        _spreadXY     = new Vector2[rows, cols];
        _scaleMul     = new float[rows, cols];
        _spreadXY_T   = new Vector2[rows, cols];
        _scaleMul_T   = new float[rows, cols];
        _baseLocalPos = new Vector3[rows, cols];

        _gapCurrent = baseGap;
        _gapTarget  = baseGap;
        _gridUniformScaleMul   = 1f;
        _gridUniformScaleMul_T = 1f;

        float pitchX = cellSize.x + baseGap.x;
        float pitchY = cellSize.y + baseGap.y;

        float totalX = cols * pitchX - baseGap.x;
        float totalY = rows * pitchY - baseGap.y;

        _centerLocal = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float lx = c * pitchX + cellSize.x * 0.5f;
            float ly = r * pitchY + cellSize.y * 0.5f;
            Vector3 local = new Vector3(lx, ly, 0f);
            if (centerAtOrigin) local -= _centerLocal;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"GateCell_{r}_{c}";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = new Vector3(cellSize.x, cellSize.y, depth);

            go.layer = CellLayer;
            var col = go.GetComponent<BoxCollider>();
            if (col) col.isTrigger = false;

            var mr = go.GetComponent<Renderer>();
            if (mr && cellMaterial) mr.sharedMaterial = cellMaterial;

            var tag = go.AddComponent<GateCell>();
            tag.Init(this, r, c);

            _cells[r, c] = go.transform;
            _baseLocalPos[r, c] = local;

            _spreadXY[r, c]   = Vector2.zero;
            _scaleMul[r, c]   = 1f;
            _spreadXY_T[r, c] = Vector2.zero;
            _scaleMul_T[r, c] = 1f;
        }

        _globalSpread     = 0f;
        _globalScaleMul   = 1f;
        _globalSpread_T   = 0f;
        _globalScaleMul_T = 1f;

        CreateOrResetPaintSurface();

        if (spreadShape == SpreadShape.GridUniform) ApplyGridUniformLayout();
        else                                       ApplyBaseLayout();

        UpdatePaintSurfaceToBounds(_ComputeStaticBounds());
    }

    public void ClearExpansion()
    {
        if (_cells == null) return;

        _globalSpread = 0f;         _globalSpread_T = 0f;
        _globalScaleMul = 1f;       _globalScaleMul_T = 1f;
        _gapCurrent = baseGap;      _gapTarget = baseGap;
        _gridUniformScaleMul = 1f;  _gridUniformScaleMul_T = 1f;

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            _spreadXY[r, c]   = Vector2.zero;
            _scaleMul[r, c]   = 1f;
            _spreadXY_T[r, c] = Vector2.zero;
            _scaleMul_T[r, c] = 1f;

            _cells[r, c].localScale = new Vector3(cellSize.x, cellSize.y, depth);
        }

        if (spreadShape == SpreadShape.GridUniform) ApplyGridUniformLayout();
        else                                       ApplyBaseLayout();

        UpdatePaintSurfaceToBounds(_ComputeStaticBounds());
    }

    /// Paint input from the brush.
    public void AddPaint(Vector3 worldPoint, float brushRadius, float metersDrawn)
    {
        if (_cells == null) return;
        if (metersDrawn <= 0f) return;
        metersDrawn = Mathf.Min(metersDrawn, Mathf.Max(0.001f, maxMetersPerSample));

        if (spreadShape == SpreadShape.GridUniform)
        {
            ApplyGridUniformPaint_Target(metersDrawn);
            return;
        }

        float kSpread = spreadPerMeter * metersDrawn;
        float kScale  = scalePerMeter  * metersDrawn;

        if (growthMode == GrowthMode.AllCellsTogether)
        {
            _globalSpread_T   = Mathf.Clamp(_globalSpread_T + kSpread, 0f, maxSpread);
            _globalScaleMul_T = Mathf.Clamp(_globalScaleMul_T + kScale, 1f, maxScaleMul);
            return;
        }

        Vector3 localHit = transform.InverseTransformPoint(worldPoint);

        float pitchX = cellSize.x + baseGap.x;
        float pitchY = cellSize.y + baseGap.y;

        int c0 = Mathf.FloorToInt((localHit.x + _centerLocal.x) / pitchX);
        int r0 = Mathf.FloorToInt((localHit.y + _centerLocal.y) / pitchY);

        int radX = Mathf.CeilToInt((brushRadius + pitchX) / pitchX);
        int radY = Mathf.CeilToInt((brushRadius + pitchY) / pitchY);

        float twoSigma2 = Mathf.Max(1e-6f, (brushRadius * brushRadius) / falloffSharpness);

        for (int dr = -radY; dr <= radY; dr++)
        {
            int r = r0 + dr; if (r < 0 || r >= rows) continue;
            for (int dc = -radX; dc <= radX; dc++)
            {
                int c = c0 + dc; if (c < 0 || c >= cols) continue;

                Vector3 cellLocal = _baseLocalPos[r, c];

                Vector2 delta = new Vector2(localHit.x - cellLocal.x, localHit.y - cellLocal.y);
                float d2 = delta.sqrMagnitude;

                float w = Mathf.Exp(-d2 / Mathf.Max(1e-6f, 2f * twoSigma2));
                if (w < 1e-4f) continue;

                Vector2 fromCenter = centerAtOrigin
                    ? new Vector2(cellLocal.x, cellLocal.y)
                    : new Vector2(cellLocal.x - _centerLocal.x, cellLocal.y - _centerLocal.y);

                Vector2 dir = OutwardDir(fromCenter);

                Vector2 addSpread = dir * (kSpread * w);
                Vector2 newTarget = _spreadXY_T[r, c] + addSpread;
                float cl = Mathf.Min(newTarget.magnitude, maxSpread);
                _spreadXY_T[r, c] = (cl > 1e-6f) ? newTarget.normalized * cl : Vector2.zero;

                _scaleMul_T[r, c] = Mathf.Clamp(_scaleMul_T[r, c] + (kScale * w), 1f, maxScaleMul);
            }
        }
    }

    // ===== GridUniform helpers =====

    private void ApplyGridUniformPaint_Target(float metersDrawn)
    {
        Vector2 target = _gapTarget + gapPerMeter * metersDrawn;
        target.x = Mathf.Clamp(target.x, baseGap.x, maxGapAbs.x);
        target.y = Mathf.Clamp(target.y, baseGap.y, maxGapAbs.y);
        _gapTarget = target;

        _gridUniformScaleMul_T = Mathf.Clamp(_gridUniformScaleMul_T + scalePerMeter * metersDrawn, 1f, maxScaleMul);
    }

    private void ApplyGridUniformLayout()
    {
        float pitchX = cellSize.x + _gapCurrent.x;
        float pitchY = cellSize.y + _gapCurrent.y;

        float totalX = cols * pitchX - _gapCurrent.x;
        float totalY = rows * pitchY - _gapCurrent.y;

        Vector3 centerNow = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;
        Vector3 scaled = new Vector3(
            cellSize.x * _gridUniformScaleMul,
            cellSize.y * _gridUniformScaleMul,
            depth     * _gridUniformScaleMul
        );

        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool boundsInit = false;

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float lx = c * pitchX + cellSize.x * 0.5f;
            float ly = r * pitchY + cellSize.y * 0.5f;
            Vector3 local = new Vector3(lx, ly, 0f);
            if (centerAtOrigin) local -= centerNow;

            var t = _cells[r, c];
            t.localPosition = local;
            t.localScale = scaled;
            t.gameObject.layer = CellLayer;

            Bounds b = new Bounds(local, scaled);
            if (!boundsInit) { localBounds = b; boundsInit = true; }
            else localBounds.Encapsulate(b);
        }

        UpdatePaintSurfaceToBounds(localBounds);
    }

    private void ApplyBaseLayout()
    {
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            _cells[r, c].localPosition = _baseLocalPos[r, c];
            _cells[r, c].localScale    = new Vector3(cellSize.x, cellSize.y, depth);
            _cells[r, c].gameObject.layer = CellLayer;
        }
    }

    // ===== Helpers (non-GridUniform modes) =====

    private Vector2 OutwardDir(Vector2 fromCenter)
    {
        float x = fromCenter.x, y = fromCenter.y;
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y);

        switch (spreadShape)
        {
            case SpreadShape.Circle:
                { float len = Mathf.Sqrt(x * x + y * y); return (len > 1e-6f) ? new Vector2(x / len, y / len) : Vector2.zero; }
            case SpreadShape.AxisDominant:
                { if (ax < 1e-6f && ay < 1e-6f) return Vector2.zero; return (ax >= ay) ? new Vector2(Mathf.Sign(x), 0f) : new Vector2(0f, Mathf.Sign(y)); }
            case SpreadShape.Square:
            default:
                { float m = Mathf.Max(ax, ay); if (m < 1e-6f) return Vector2.zero; return new Vector2(x / m, y / m); }
        }
    }

    private void CleanupChildren()
    {
        var doomed = new List<Transform>();
        foreach (Transform child in transform) doomed.Add(child);
        foreach (var t in doomed) DestroyImmediate(t.gameObject);

        _paintSurfaceGO = null;
        _paintCol = null;
        _paintMarker = null;
    }

    // ===== Big Paint Surface creation & bounds syncing =====

    private void CreateOrResetPaintSurface()
    {
        _paintSurfaceGO = new GameObject("GatePaintSurface");
        _paintSurfaceGO.transform.SetParent(transform, false);
        _paintSurfaceGO.layer = PaintLayer;

        _paintCol = _paintSurfaceGO.AddComponent<BoxCollider>();
        _paintCol.isTrigger = true;

        var rb = _paintSurfaceGO.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Marker so the brush system can detect this as a paintable surface.
        _paintMarker = _paintSurfaceGO.AddComponent<PaintSurfaceMarker>();

        // >>> NEW: add a GateCell here so MouseBrushPainter will find the grid even when hitting the big surface.
        var tag = _paintSurfaceGO.AddComponent<GateCell>();
        tag.Init(this, -1, -1);

        // very thin Z initially; will be resized in UpdatePaintSurfaceToBounds.
        _paintCol.center = Vector3.zero;
        _paintCol.size = new Vector3(0.1f, 0.1f, Mathf.Max(0.005f, depth * 0.5f));
    }

    private void UpdatePaintSurfaceToBounds(Bounds localBounds)
    {
        if (_paintCol == null) return;

        const float pad = 0.0025f;

        Vector3 size = localBounds.size;
        size.x += pad * 2f;
        size.y += pad * 2f;
        size.z = Mathf.Max(depth, size.z);

        _paintSurfaceGO.transform.localPosition = localBounds.center;
        _paintCol.size = size;
        _paintCol.center = Vector3.zero;
        _paintSurfaceGO.layer = PaintLayer;
    }

    private Bounds _ComputeStaticBounds()
    {
        if (rows <= 0 || cols <= 0) return new Bounds(Vector3.zero, Vector3.zero);

        Vector2 gap = (spreadShape == SpreadShape.GridUniform) ? _gapCurrent : baseGap;

        float pitchX = cellSize.x + gap.x;
        float pitchY = cellSize.y + gap.y;
        float totalX = cols * pitchX - gap.x;
        float totalY = rows * pitchY - gap.y;

        Vector3 centerNow = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;
        Vector3 min = centerAtOrigin ? new Vector3(-centerNow.x, -centerNow.y, -depth * 0.5f) : new Vector3(0f, 0f, -depth * 0.5f);
        Vector3 size = new Vector3(totalX, totalY, depth);

        return new Bounds(min + size * 0.5f, size);
    }

    void OnDrawGizmosSelected()
    {
        if (!gizmosCenter) return;
        Gizmos.color = Color.cyan;
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Vector2 gap = Application.isPlaying && spreadShape == SpreadShape.GridUniform ? _gapCurrent : baseGap;
        float pitchX = cellSize.x + gap.x;
        float pitchY = cellSize.y + gap.y;
        float totalX = cols * pitchX - gap.x;
        float totalY = rows * pitchY - gap.y;

        Vector3 centerNow = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;
        Vector3 half = new Vector3(totalX, totalY, 0.001f) * 0.5f;

        Gizmos.DrawWireCube(centerAtOrigin ? Vector3.zero : centerNow, half * 2f);
        Gizmos.matrix = m;
    }
}
