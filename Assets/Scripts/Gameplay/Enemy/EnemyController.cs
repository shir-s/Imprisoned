using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyController : MonoBehaviour
{
    public enum EnemyState
    {
        Patrol,
        Chase
    }

    [Header("References")]
    [SerializeField] private Transform player;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float directionChangeInterval = 2f;

    [Header("Detection")]
    [SerializeField] private float detectionRadius = 6f;
    [SerializeField] private float chaseDuration = 4f;

    [Header("Collision")]
    [SerializeField] private LayerMask wallLayerMask; // set to "Wall" layer in Inspector

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 10f;

    private EnemyState _state = EnemyState.Patrol;
    private Vector3 _currentDirection;
    private float _directionTimer;
    private float _chaseTimer;
    private float _baseY;

    private BoxCollider _collider;

    private void Start()
    {
        _collider = GetComponent<BoxCollider>();

        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        _baseY = transform.position.y;
        PickNewRandomDirection();
        _directionTimer = directionChangeInterval;
    }

    private void Update()
    {
        if (!player) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (_state)
        {
            case EnemyState.Patrol:
                if (distanceToPlayer <= detectionRadius)
                {
                    _state = EnemyState.Chase;
                    _chaseTimer = chaseDuration;
                }
                PatrolUpdate();
                break;

            case EnemyState.Chase:
                _chaseTimer -= Time.deltaTime;
                if (_chaseTimer <= 0f)
                {
                    _state = EnemyState.Patrol;
                    PickNewRandomDirection();
                    _directionTimer = directionChangeInterval;
                    PatrolUpdate();
                }
                else
                {
                    ChaseUpdate();
                }
                break;
        }
    }

    private void PatrolUpdate()
    {
        _directionTimer -= Time.deltaTime;
    
        if (IsDirectionBlocked(_currentDirection))
        {
            PickNewRandomDirection();
            _directionTimer = directionChangeInterval; 
        }

        if (_directionTimer <= 0f)
        {
            PickNewRandomDirection();
            _directionTimer = directionChangeInterval;
        }

        MoveInDirection(_currentDirection);
    }

    private bool IsDirectionBlocked(Vector3 direction)
    {
        Vector3 targetPos = transform.position + direction * 0.5f;
        Vector3 halfExtents = _collider.bounds.extents;

        return Physics.CheckBox(
            targetPos,
            halfExtents,
            Quaternion.identity,
            wallLayerMask
        );
    }


    private void ChaseUpdate()
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = toPlayer.normalized;
            MoveInDirection(dir);
        }
    }

    private void MoveInDirection(Vector3 direction)
    {
        // rotate to face movement direction (only around Y axis)
        if (direction.sqrMagnitude > 0.0001f)
        {
            Vector3 flatDir = new Vector3(direction.x, 0f, direction.z);
            Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotationSpeed * Time.deltaTime
            );
        }

        Vector3 delta = direction * moveSpeed * Time.deltaTime;
        Vector3 targetPos = transform.position + delta;
        targetPos.y = _baseY;

        // half size of the box for CheckBox
        Vector3 halfExtents = _collider.bounds.extents;

        // check if at the target position we would overlap a Wall
        bool hitWall = Physics.CheckBox(
            targetPos,
            halfExtents,
            Quaternion.identity,
            wallLayerMask
        );

        if (!hitWall)
        {
            transform.position = targetPos;
        }
        // else: do nothing this frame (enemy is blocked by wall)
    }

    private void PickNewRandomDirection()
    {
        int choice = Random.Range(0, 4);
        switch (choice)
        {
            case 0: _currentDirection = Vector3.forward; break;
            case 1: _currentDirection = Vector3.back;    break;
            case 2: _currentDirection = Vector3.right;   break;
            case 3: _currentDirection = Vector3.left;    break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
