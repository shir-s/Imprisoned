// FILEPATH: Assets/Scripts/Interaction/ClickableMover.cs
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ClickableMover : MonoBehaviour
{
    public enum MoveDirection { Forward, Backward, Left, Right, Custom }

    [Header("Movement Settings")]
    [SerializeField] private MoveDirection direction = MoveDirection.Forward;
    [SerializeField] private Vector3 customDirection = Vector3.forward;
    [SerializeField] private float moveDistance = 2f;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private bool pingPong = false; // go back after finishing

    private Vector3 _startPos;
    private Vector3 _targetPos;
    private bool _movingForward = true;
    private bool _isMoving = false;

    void Start()
    {
        _startPos = transform.position;
        _targetPos = _startPos + GetWorldDirection() * moveDistance;
    }

    void OnMouseDown()
    {
        // Unity calls this when clicking a collider with a Camera ray
        if (!_isMoving)
        {
            _isMoving = true;
        }
    }

    void Update()
    {
        if (!_isMoving) return;

        Vector3 target = _movingForward ? _targetPos : _startPos;
        transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, target) < 0.001f)
        {
            if (pingPong)
            {
                _movingForward = !_movingForward;
            }
            else
            {
                _isMoving = false;
            }
        }
    }

    private Vector3 GetWorldDirection()
    {
        switch (direction)
        {
            case MoveDirection.Forward:  return Vector3.forward;
            case MoveDirection.Backward: return Vector3.back;
            case MoveDirection.Left:     return Vector3.left;
            case MoveDirection.Right:    return Vector3.right;
            case MoveDirection.Custom:   return customDirection.normalized;
            default: return Vector3.forward;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 start = Application.isPlaying ? _startPos : transform.position;
        Vector3 dir = GetWorldDirection();
        Gizmos.DrawLine(start, start + dir * moveDistance);
        Gizmos.DrawSphere(start + dir * moveDistance, 0.1f);
    }
#endif
}
