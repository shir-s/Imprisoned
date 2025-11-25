using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based A* follower that chases a player,
/// trying to follow the painted trail on PaintTrailGrid.
/// 1) First it tries to find a path using ONLY painted cells (trail-following).
/// 2) If that fails, it falls back to a normal path that may cut across empty cells.
/// </summary>
[DisallowMultipleComponent]
public class MonsterAStarFollower : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform target;            // Player - auto found
    [SerializeField] private PaintTrailGrid trailGrid;    // The Map's grid

    [Header("Target Finding")]
    [Tooltip("Automatically find the player if none assigned.")]
    [SerializeField] private bool autoFindTarget = true;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float arriveThreshold = 0.1f;
    [Tooltip("Seconds between path recalculations.")]
    [SerializeField] private float pathUpdateInterval = 0.3f;

    [Header("Cost Settings")]
    [Tooltip("Base movement cost for an empty (unpainted) cell.")]
    [SerializeField] private float baseCost = 5f;
    [Tooltip("Movement cost when the cell has paint (lower = prefers trail).")]
    [SerializeField] private float paintedCost = 0.5f;

    [Header("Trail Following")]
    [Tooltip("First try to find a path that uses only painted cells. If none exists, fall back to normal A*.")]
    [SerializeField] private bool tryPaintOnlyPathFirst = true;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private Color pathColor = Color.magenta;

    private List<Vector3> _currentPathWorld = new List<Vector3>();
    private int _currentPathIndex = 0;
    private float _nextPathTime = 0f;

    private void Reset()
    {
        moveSpeed = 5f;
        pathUpdateInterval = 0.3f;
    }

    // -------------------------------------------------------
    // UPDATE
    // -------------------------------------------------------
    private void Update()
    {
        // Auto-find the player on spawn or respawn
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            TryAutoFindTarget();
        }

        if (target == null || trailGrid == null)
            return;

        // Recalculate path every X seconds
        if (Time.time >= _nextPathTime)
        {
            _nextPathTime = Time.time + pathUpdateInterval;
            RecalculatePath();
        }

        FollowPath();
    }

    // -------------------------------------------------------
    // AUTO TARGET FINDING
    // -------------------------------------------------------
    private void TryAutoFindTarget()
    {
        if (!autoFindTarget)
            return;

        var mover = FindObjectOfType<MovementPaintController>();
        if (mover != null)
        {
            target = mover.transform;
            Debug.Log("[MonsterAStarFollower] Auto-found player via MovementPaintController.", this);
        }
    }

    // -------------------------------------------------------
    // PATHFINDING
    // -------------------------------------------------------
    private void RecalculatePath()
    {
        if (!trailGrid.WorldToGrid(transform.position, out int startX, out int startY))
            return;
        if (!trailGrid.WorldToGrid(target.position, out int goalX, out int goalY))
            return;

        int gridSize = trailGrid.Resolution;
        bool[,] painted = trailGrid.Painted;

        List<Vector2Int> pathCells = null;

        // 1) Try "paint-only" path: the monster walks strictly on painted cells (if trail connects).
        if (tryPaintOnlyPathFirst && painted != null)
        {
            pathCells = FindPathAStar(
                startX, startY,
                goalX, goalY,
                gridSize,
                painted,
                paintOnlyMode: true
            );
        }

        // 2) Fallback: normal A* that may step on empty cells as well.
        if (pathCells == null || pathCells.Count == 0)
        {
            pathCells = FindPathAStar(
                startX, startY,
                goalX, goalY,
                gridSize,
                painted,
                paintOnlyMode: false
            );
        }

        _currentPathWorld.Clear();
        _currentPathIndex = 0;

        if (pathCells == null || pathCells.Count == 0)
            return;

        foreach (var cell in pathCells)
        {
            if (trailGrid.GridToWorld(cell.x, cell.y, out Vector3 wp))
            {
                wp.y = transform.position.y; // keep monster above the surface
                _currentPathWorld.Add(wp);
            }
        }
    }

    // -------------------------------------------------------
    // FOLLOW THE PATH
    // -------------------------------------------------------
    private void FollowPath()
    {
        if (_currentPathWorld.Count == 0)
            return;

        if (_currentPathIndex >= _currentPathWorld.Count)
            return;

        Vector3 targetPos = _currentPathWorld[_currentPathIndex];
        Vector3 pos = transform.position;
        Vector3 to = targetPos - pos;
        to.y = 0f;

        float dist = to.magnitude;
        if (dist < arriveThreshold)
        {
            _currentPathIndex++;
            return;
        }

        Vector3 dir = to.normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;

        if (dir.sqrMagnitude > 0.001f)
        {
            transform.forward = dir;
        }
    }

    // -------------------------------------------------------
    // A* IMPLEMENTATION
    // -------------------------------------------------------
    private List<Vector2Int> FindPathAStar(
        int startX, int startY,
        int goalX, int goalY,
        int gridSize,
        bool[,] paintedGrid,
        bool paintOnlyMode)
    {
        const float INF = 1e9f;

        float[,] gCost = new float[gridSize, gridSize];
        bool[,] closed = new bool[gridSize, gridSize];
        Vector2Int[,] parent = new Vector2Int[gridSize, gridSize];

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                gCost[x, y] = INF;
                parent[x, y] = new Vector2Int(-1, -1);
            }
        }

        Vector2Int start = new Vector2Int(startX, startY);
        Vector2Int goal = new Vector2Int(goalX, goalY);

        List<Vector2Int> open = new List<Vector2Int>();
        gCost[startX, startY] = 0f;
        open.Add(start);

        while (open.Count > 0)
        {
            // Find lowest f = g + h
            int bestIndex = 0;
            float bestF = INF;

            for (int i = 0; i < open.Count; i++)
            {
                Vector2Int c = open[i];
                float g = gCost[c.x, c.y];
                float h = Heuristic(c, goal);
                float f = g + h;

                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);

            if (current == goal)
                return ReconstructPath(parent, goal);

            closed[current.x, current.y] = true;

            foreach (var n in GetNeighbors(current, gridSize))
            {
                int nx = n.x;
                int ny = n.y;

                if (closed[nx, ny])
                    continue;

                bool isPainted = paintedGrid != null && paintedGrid[nx, ny];

                // In paint-only mode: we only allow stepping onto painted cells,
                // except start/goal cells which may be empty.
                if (paintOnlyMode &&
                    !isPainted &&
                    !(nx == goalX && ny == goalY) &&
                    !(nx == startX && ny == startY))
                {
                    continue;
                }

                float stepCost = isPainted ? paintedCost : baseCost;
                float tentativeG = gCost[current.x, current.y] + stepCost;

                if (tentativeG >= gCost[nx, ny])
                    continue;

                gCost[nx, ny] = tentativeG;
                parent[nx, ny] = current;

                if (!open.Contains(n))
                    open.Add(n);
            }
        }

        return null;
    }

    private float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private IEnumerable<Vector2Int> GetNeighbors(Vector2Int c, int size)
    {
        int x = c.x;
        int y = c.y;

        if (x > 0)         yield return new Vector2Int(x - 1, y);
        if (x < size - 1)  yield return new Vector2Int(x + 1, y);
        if (y > 0)         yield return new Vector2Int(x, y - 1);
        if (y < size - 1)  yield return new Vector2Int(x, y + 1);
    }

    private List<Vector2Int> ReconstructPath(Vector2Int[,] parent, Vector2Int goal)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Vector2Int c = goal;

        while (c.x != -1 && c.y != -1)
        {
            path.Add(c);
            c = parent[c.x, c.y];
        }

        path.Reverse();
        return path;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || _currentPathWorld == null || _currentPathWorld.Count < 2)
            return;

        Gizmos.color = pathColor;
        for (int i = 0; i < _currentPathWorld.Count - 1; i++)
        {
            Gizmos.DrawLine(
                _currentPathWorld[i] + Vector3.up * 0.2f,
                _currentPathWorld[i + 1] + Vector3.up * 0.2f
            );
        }
    }
#endif
}
