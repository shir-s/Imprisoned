using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based A* follower that chases a player,
/// trying to follow the painted trail on PaintTrailGrid.
/// 1) First it tries to find a path using ONLY painted cells.
/// 2) If that fails, it falls back to a normal path.
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
    
    [Header("Surface Sticking")]
    [SerializeField] private LayerMask surfaceLayers;        // Layers that represent the board/surface
    [SerializeField] private float surfaceRayHeight = 5f;    // Height above the surface from which to shoot the ray downward
    [SerializeField] private float alignRotationSpeed = 10f; // How fast to align rotation with the surface normal

    // --- runtime ---
    private Rigidbody _rb;
    private Collider _col;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        if (_rb != null)
        {
            // ✦ Let physics control the Y axis (gravity / vertical motion)
            _rb.useGravity = true;
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Prevent the monster from tipping over on its side
            _rb.constraints = RigidbodyConstraints.FreezeRotationX |
                              RigidbodyConstraints.FreezeRotationZ;
        }
    }

    private void Reset()
    {
        moveSpeed = 5f;
        pathUpdateInterval = 0.3f;
    }

    // -------------------------------------------------------
    // UPDATE – only target & path logic, no physics movement
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
    }

    // -------------------------------------------------------
    // FIXED UPDATE – physics movement
    // -------------------------------------------------------
    private void FixedUpdate()
    {
        if (_rb != null)
        {
            // 💡 Here we "lock" physics on the X,Z axes:
            // We allow only Y velocity (gravity / collisions with the board)
#if UNITY_6000_0_OR_NEWER    // if you're on a Unity version that has linearVelocity
            Vector3 v = _rb.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            _rb.linearVelocity = v;
#else
            Vector3 v = _rb.velocity;
            v.x = 0f;
            v.z = 0f;
            _rb.velocity = v;
#endif
        }

        FollowPathPhysics(Time.fixedDeltaTime);
    }

    private void FollowPathPhysics(float dt)
    {
        if (_rb == null)
            return;

        if (_currentPathWorld == null || _currentPathWorld.Count == 0)
            return;

        if (_currentPathIndex < 0 || _currentPathIndex >= _currentPathWorld.Count)
            return;

        Vector3 pos = _rb.position;
        Vector3 targetPos = _currentPathWorld[_currentPathIndex];

        // Move only in the horizontal plane (XZ)
        Vector3 flatFrom = new Vector3(pos.x, 0f, pos.z);
        Vector3 flatTo   = new Vector3(targetPos.x, 0f, targetPos.z);
        Vector3 to       = flatTo - flatFrom;

        float dist = to.magnitude;
        if (dist < arriveThreshold)
        {
            _currentPathIndex++;
            return;
        }

        Vector3 dir = to.normalized;

        // Compute step for this physics frame
        float step = moveSpeed * dt;
        if (step > dist)
            step = dist;

        // XZ follow A*, Y is controlled by physics
        Vector3 newPos = new Vector3(
            pos.x + dir.x * step,
            pos.y,                  // Y stays as determined by physics
            pos.z + dir.z * step
        );

        // Before moving – sync rotation to the surface
        newPos = StickToSurface(newPos, dt);

        // Move via Rigidbody
        _rb.MovePosition(newPos);

        // Rotate to face movement direction along the surface
        if (dir.sqrMagnitude > 0.001f)
        {
            Vector3 projectedForward = Vector3.ProjectOnPlane(dir, transform.up).normalized;
            if (projectedForward.sqrMagnitude > 0.001f)
                transform.forward = projectedForward;
        }
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
    // PATHFINDING (unchanged)
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

        // 1) Try "paint-only" path
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

        // 2) Fallback: normal A*
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
                // Y comes from physics, so we don't care here
                wp.y = transform.position.y;
                _currentPathWorld.Add(wp);
            }
        }
    }

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
    
    private Vector3 StickToSurface(Vector3 tentativePos, float dt)
    {
        if (surfaceLayers.value == 0 || _rb == null)
            return tentativePos;

        Vector3 rayOrigin = tentativePos + Vector3.up * surfaceRayHeight;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, surfaceRayHeight * 2f, surfaceLayers))
        {
            // Here we do NOT change the Y position (it stays driven by physics),
            // we only align the rotation to match the surface normal.
            Quaternion currentRot = transform.rotation;
            Quaternion targetRot = Quaternion.FromToRotation(transform.up, hit.normal) * currentRot;

            Quaternion smoothRot = Quaternion.Slerp(
                currentRot,
                targetRot,
                alignRotationSpeed * dt
            );

            _rb.MoveRotation(smoothRot);

            // Return a position with XZ from A*, and Y from physics
            return new Vector3(tentativePos.x, _rb.position.y, tentativePos.z);
        }

        return tentativePos;
    }
}
