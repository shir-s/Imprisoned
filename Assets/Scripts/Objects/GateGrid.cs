// FILEPATH: Assets/Scripts/Gate/GateGrid.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GateGrid : MonoBehaviour
{
    public enum GrowthMode
    {
        OnlyPaintedCells, // affect cells near the brush (gaussian falloff)
        AllCellsTogether  // any paint grows/spreads all cells uniformly
    }

    public enum SpreadShape
    {
        Circle,        // L2 radial spread (round front)
        AxisDominant,  // “diamond split into 4 triangles” look (moves only along dominant axis)
        Square,        // L∞ direction (square front), local push while keeping quadrant look
        GridUniform    // NEW: keep grid topology; increase base gaps uniformly (true “square gate widener”)
    }

    [Header("Grid Size")]
    [SerializeField] private int rows = 100;
    [SerializeField] private int cols = 100;

    [Header("Cell Geometry")]
    [SerializeField] private Vector2 cellSize = new Vector2(0.08f, 0.08f); // in-plane X by Y
    [SerializeField] private float depth = 0.08f;                          // Z thickness (single layer)
    [SerializeField] private Vector2 baseGap = new Vector2(0.01f, 0.01f);  // initial spacing between cells

    [Header("Layers & Rendering")]
    [Tooltip("Layer index (0..31) for spawned cubes. Ensure your MouseBrushPainter.surfaceMask includes this.")]
    [SerializeField, Range(0,31)] private int cellLayer = 0;
    [SerializeField] private bool centerAtOrigin = true;
    [SerializeField] private Material cellMaterial;

    [Header("Paint → Expansion (Local/Global Push Modes)")]
    [SerializeField] private GrowthMode growthMode = GrowthMode.OnlyPaintedCells;
    [SerializeField] private SpreadShape spreadShape = SpreadShape.Circle;
    [SerializeField] private float spreadPerMeter = 0.02f;   // meters pushed outward per meter painted
    [SerializeField] private float scalePerMeter  = 0.25f;   // multiplicative scale gain per meter painted
    [SerializeField] private float falloffSharpness = 3.0f;  // (OnlyPaintedCells) gaussian-like falloff
    [SerializeField] private float maxScaleMul = 3.0f;       // clamp
    [SerializeField] private float maxSpread   = 4.0f;       // clamp (meters)

    [Header("GridUniform (Gap-Widening) Settings")]
    [Tooltip("Meters added to X/Y base gap per meter drawn (only used in GridUniform).")]
    [SerializeField] private Vector2 gapPerMeter = new Vector2(0.20f, 0.20f);
    [Tooltip("Maximum absolute X/Y gap (example: X=1, Y=1 means you can expand from 0.01 → 1).")]
    [SerializeField] private Vector2 maxGapAbs = new Vector2(1.0f, 1.0f);

    [Header("Debug")]
    [SerializeField] private bool gizmosCenter = true;

    // Internal
    private Transform[,] _cells;
    private Vector2[,] _spreadXYLocal; // per-cell outward push (OnlyPaintedCells/Square/AxisDominant/Circle)
    private float[,] _scaleMulLocal;   // per-cell scale multiplier (OnlyPaintedCells)

    // Global accumulators (AllCellsTogether – push/scale)
    private float _globalSpread;       // meters
    private float _globalScaleMul = 1f;

    // GridUniform accumulators (true gap widening)
    private Vector2 _gapCurrent;       // current absolute gap (starts at baseGap, grows to <= maxGapAbs)
    private float _gridUniformScaleMul = 1f; // optional global scale growth in GridUniform

    private Vector3[,] _baseLocalPos;  // initial layout (for non-GridUniform modes)
    private Vector3 _centerLocal;

    public int CellLayer => Mathf.Clamp(cellLayer, 0, 31);

    void Awake()
    {
        Build();
    }

    // Build grid of cubes (each with BoxCollider)
    public void Build()
    {
        CleanupChildren();

        _cells          = new Transform[rows, cols];
        _spreadXYLocal  = new Vector2[rows, cols];
        _scaleMulLocal  = new float[rows, cols];
        _baseLocalPos   = new Vector3[rows, cols];

        // initialize GridUniform gap
        _gapCurrent = baseGap;
        _gridUniformScaleMul = 1f;

        // layout using base gap (for initial placement & for non-GridUniform modes)
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

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // MeshRenderer + BoxCollider
            go.name = $"GateCell_{r}_{c}";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = new Vector3(cellSize.x, cellSize.y, depth);

            // ensure spawned cubes are on the intended layer & raycastable
            go.layer = CellLayer;
            var col = go.GetComponent<BoxCollider>();
            if (col) col.isTrigger = false;

            // material
            var mr = go.GetComponent<Renderer>();
            if (mr && cellMaterial) mr.sharedMaterial = cellMaterial;

            // tag (optional, used by painter hit routing)
            var tag = go.AddComponent<GateCell>();
            tag.Init(this, r, c);

            _cells[r, c] = go.transform;
            _baseLocalPos[r, c] = local;
            _spreadXYLocal[r, c] = Vector2.zero;
            _scaleMulLocal[r, c] = 1f;
        }

        // reset non-GridUniform globals
        _globalSpread = 0f;
        _globalScaleMul = 1f;

        // If starting in GridUniform mode, make sure initial layout matches _gapCurrent (baseGap)
        if (spreadShape == SpreadShape.GridUniform)
            ApplyGridUniformLayout();
    }

    public void ClearExpansion()
    {
        if (_cells == null) return;

        // reset accumulators
        _globalSpread = 0f;
        _globalScaleMul = 1f;
        _gapCurrent = baseGap;
        _gridUniformScaleMul = 1f;

        // reset transforms
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            _spreadXYLocal[r, c] = Vector2.zero;
            _scaleMulLocal[r, c] = 1f;

            _cells[r, c].localScale = new Vector3(cellSize.x, cellSize.y, depth);
        }

        if (spreadShape == SpreadShape.GridUniform)
            ApplyGridUniformLayout();
        else
            ApplyBaseLayout();
    }

    /// <summary>
    /// Paint input from the brush. Increases spread/scale around the hit.
    /// </summary>
    public void AddPaint(Vector3 worldPoint, float brushRadius, float metersDrawn)
    {
        if (_cells == null || metersDrawn <= 0f) return;

        // Special case: GridUniform (true “square gate widener”) — grow base gaps uniformly.
        if (spreadShape == SpreadShape.GridUniform)
        {
            ApplyGridUniformPaint(metersDrawn);
            return;
        }

        float kSpread = spreadPerMeter * metersDrawn;
        float kScale  = scalePerMeter  * metersDrawn;

        if (growthMode == GrowthMode.AllCellsTogether)
        {
            // ---- GLOBAL MODE (non-GridUniform) ----
            _globalSpread = Mathf.Min(maxSpread, _globalSpread + kSpread);
            _globalScaleMul = Mathf.Min(maxScaleMul, _globalScaleMul * (1f + kScale));

            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                Vector3 basePos = _baseLocalPos[r, c];

                Vector2 fromCenter = centerAtOrigin
                    ? new Vector2(basePos.x, basePos.y)
                    : new Vector2(basePos.x - _centerLocal.x, basePos.y - _centerLocal.y);

                Vector2 dir = OutwardDir(fromCenter);
                Vector3 pos = basePos + new Vector3(dir.x, dir.y, 0f) * _globalSpread;
                _cells[r, c].localPosition = pos;

                _cells[r, c].localScale = new Vector3(
                    cellSize.x * _globalScaleMul,
                    cellSize.y * _globalScaleMul,
                    depth     * _globalScaleMul
                );

                _cells[r, c].gameObject.layer = CellLayer;
            }
            return;
        }

        // ---- LOCAL MODE (OnlyPaintedCells) ----
        Vector3 localHit = transform.InverseTransformPoint(worldPoint);

        // index neighborhood using base layout (keeps the region small)
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

                // outward from center, shaped per SpreadShape
                Vector2 fromCenter = centerAtOrigin
                    ? new Vector2(cellLocal.x, cellLocal.y)
                    : new Vector2(cellLocal.x - _centerLocal.x, cellLocal.y - _centerLocal.y);

                Vector2 dir = OutwardDir(fromCenter);

                Vector2 addSpread = dir * (kSpread * w);
                Vector2 newSpread = _spreadXYLocal[r, c] + addSpread;
                float cl = Mathf.Min(newSpread.magnitude, maxSpread);
                _spreadXYLocal[r, c] = (cl > 1e-6f) ? newSpread.normalized * cl : Vector2.zero;

                _scaleMulLocal[r, c] = Mathf.Min(maxScaleMul, _scaleMulLocal[r, c] * (1f + kScale * w));
            }
        }

        // Apply to affected neighborhood only
        int applyY0 = Mathf.Max(0, r0 - radY - 1);
        int applyY1 = Mathf.Min(rows - 1, r0 + radY + 1);
        int applyX0 = Mathf.Max(0, c0 - radX - 1);
        int applyX1 = Mathf.Min(cols - 1, c0 + radX + 1);

        for (int r = applyY0; r <= applyY1; r++)
        for (int c = applyX0; c <= applyX1; c++)
        {
            Vector2 s = _spreadXYLocal[r, c];
            Vector3 pos = _baseLocalPos[r, c] + new Vector3(s.x, s.y, 0f);
            _cells[r, c].localPosition = pos;

            float mul = _scaleMulLocal[r, c];
            _cells[r, c].localScale = new Vector3(cellSize.x * mul, cellSize.y * mul, depth * mul);

            _cells[r, c].gameObject.layer = CellLayer;
        }
    }

    // ===== GridUniform (true “square gate widener”) =====

    private void ApplyGridUniformPaint(float metersDrawn)
    {
        // Increase absolute gaps uniformly, independent of hit location.
        // If you want growth only when GrowthMode == AllCellsTogether, keep this as-is.
        // (You can also weight by falloff if you ever want a semi-local uniform effect.)
        Vector2 target = _gapCurrent + gapPerMeter * metersDrawn;

        // clamp to max absolute gaps
        target.x = Mathf.Clamp(target.x, baseGap.x, maxGapAbs.x);
        target.y = Mathf.Clamp(target.y, baseGap.y, maxGapAbs.y);
        _gapCurrent = target;

        // Optional: let cubes also scale up globally (keeps the “massive” feeling)
        _gridUniformScaleMul = Mathf.Min(maxScaleMul, _gridUniformScaleMul * (1f + scalePerMeter * metersDrawn));

        ApplyGridUniformLayout();
    }

    private void ApplyGridUniformLayout()
    {
        // Recompute layout from rows/cols, using current absolute gap (_gapCurrent).
        float pitchX = cellSize.x + _gapCurrent.x;
        float pitchY = cellSize.y + _gapCurrent.y;

        float totalX = cols * pitchX - _gapCurrent.x;
        float totalY = rows * pitchY - _gapCurrent.y;

        Vector3 centerNow = centerAtOrigin ? new Vector3(totalX * 0.5f, totalY * 0.5f, 0f) : Vector3.zero;

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float lx = c * pitchX + cellSize.x * 0.5f;
            float ly = r * pitchY + cellSize.y * 0.5f;
            Vector3 local = new Vector3(lx, ly, 0f);
            if (centerAtOrigin) local -= centerNow;

            _cells[r, c].localPosition = local;

            _cells[r, c].localScale = new Vector3(
                cellSize.x * _gridUniformScaleMul,
                cellSize.y * _gridUniformScaleMul,
                depth     * _gridUniformScaleMul
            );

            _cells[r, c].gameObject.layer = CellLayer;
        }
    }

    private void ApplyBaseLayout()
    {
        // Put cells back to their original base positions (_baseLocalPos)
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            _cells[r, c].localPosition = _baseLocalPos[r, c];
            _cells[r, c].localScale    = new Vector3(cellSize.x, cellSize.y, depth);
            _cells[r, c].gameObject.layer = CellLayer;
        }
    }

    // ===== Helpers (non-GridUniform modes) =====

    /// <summary>
    /// Direction of outward motion from center, shaped as:
    /// - Circle: normalized vector (L2) → radial spread (round)
    /// - AxisDominant: move purely along the dominant axis (splits area into 4 triangles)
    /// - Square: L∞-normalized direction → square front while preserving relative direction
    /// </summary>
    private Vector2 OutwardDir(Vector2 fromCenter)
    {
        float x = fromCenter.x;
        float y = fromCenter.y;
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);

        switch (spreadShape)
        {
            case SpreadShape.Circle:
            {
                float len = Mathf.Sqrt(x * x + y * y);
                return (len > 1e-6f) ? new Vector2(x / len, y / len) : Vector2.zero;
            }
            case SpreadShape.AxisDominant:
            {
                if (ax < 1e-6f && ay < 1e-6f) return Vector2.zero;
                if (ax >= ay) return new Vector2(Mathf.Sign(x), 0f);
                else          return new Vector2(0f, Mathf.Sign(y));
            }
            case SpreadShape.Square:
            default:
            {
                // L∞ normalization: preserves relative “angle class”; square isochrones.
                float m = Mathf.Max(ax, ay);
                if (m < 1e-6f) return Vector2.zero;
                return new Vector2(x / m, y / m);
            }
        }
    }

    private void CleanupChildren()
    {
        var doomed = new List<Transform>();
        foreach (Transform child in transform) doomed.Add(child);
        foreach (var t in doomed) DestroyImmediate(t.gameObject);
    }

    void OnDrawGizmosSelected()
    {
        if (!gizmosCenter) return;
        Gizmos.color = Color.cyan;
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        // draw current bounds if in play mode & GridUniform; otherwise base
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
