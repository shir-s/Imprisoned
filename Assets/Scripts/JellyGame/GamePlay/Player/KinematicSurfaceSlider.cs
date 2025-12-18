using JellyGame.GamePlay.Audio.Core;
using UnityEngine;

public class KinematicSurfaceSlider : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Distance from the floor to the object's pivot/center.")]
    [SerializeField] private float hoverHeight = 0.5f; 
    [SerializeField] private LayerMask floorLayer;
    [Tooltip("Y-level to respawn or destroy the object if it falls too far.")]
    [SerializeField] private float fallLimit = -50f;

    [Header("Physics Settings")]
    [SerializeField] private float gravityForce = 50f; 
    [SerializeField] private float maxSpeed = 20f;
    
    [Header("Friction")]
    [Tooltip("Friction when on the map (Stops you from sliding on flat ground).")]
    [SerializeField] private float groundDrag = 5f;
    [Tooltip("Air resistance. Keep this low so you fall naturally.")]
    [SerializeField] private float airDrag = 0.5f;

    private Vector3 _velocity;
    private bool _isGrounded;
    private bool _isMoving = false;
    private AudioSourceWrapper _movementSound;

    public void SetHoverHeight(float value)
    {
        hoverHeight = Mathf.Max(0.01f, value);
    }
    
    private void Update()
    {
        // 1. Check for the floor
        // We cast a ray from above the object downwards
        Vector3 rayOrigin = transform.position;
        rayOrigin.y += 2f; // Start ray slightly above object
        Ray ray = new Ray(rayOrigin, Vector3.down);
        
        // We look for ground within a reasonable distance (e.g., 5 units down)
        // If we are grounded, hit info is stored in 'hit'
        if (Physics.Raycast(ray, out RaycastHit hit, 10f, floorLayer))
        {
            HandleGrounded(hit);
        }
        else
        {
            HandleAirborne();
        }

        // Respawn check (Optional)
        if (transform.position.y < fallLimit)
        {
            // Reset position logic here if you want, e.g.:
            // transform.position = Vector3.up * 5;
            // _velocity = Vector3.zero;
        }
        
        UpdateMovementSound();
    }
    
    private void UpdateMovementSound()
    {
        float movementThreshold = 0.1f;
        bool currentlyMoving = _velocity.magnitude > movementThreshold;

        if (currentlyMoving && !_isMoving)
        {
            _isMoving = true;
            StartMovementSound();
        }
        else if (!currentlyMoving && _isMoving)
        {
            _isMoving = false;
            StopMovementSound();
        }
    }
    
    private void StartMovementSound()
    {
        if (_movementSound != null)
            return;

        var soundManager = SoundManager.Instance;
        if (soundManager == null)
            return;
        
        var config = soundManager.GetType()
            .GetMethod("PlaySound", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        _movementSound = SoundManager.Instance.PlayLoopingSound("SlimeWalk",
            transform
        );
    }
    private void StopMovementSound()
    {
        if (_movementSound == null)
            return;

        _movementSound.Reset();
        _movementSound.gameObject.SetActive(false);
        SoundPool.Instance.Return(_movementSound);
        _movementSound = null;
    }



    private void HandleGrounded(RaycastHit hit)
    {
        _isGrounded = true;

        // --- A. ACCELERATION (Slope Logic) ---
        // Calculate gravity along the slope
        Vector3 gravityProjected = Vector3.ProjectOnPlane(Vector3.down, hit.normal);
        
        // Apply acceleration
        _velocity += gravityProjected * gravityForce * Time.deltaTime;

        // Apply Ground Friction
        _velocity = Vector3.Lerp(_velocity, Vector3.zero, groundDrag * Time.deltaTime);
        _velocity = Vector3.ClampMagnitude(_velocity, maxSpeed);

        // --- B. MOVEMENT (Projected) ---
        // Move along the surface plane
        Vector3 moveDir = Vector3.ProjectOnPlane(_velocity, hit.normal);
        transform.position += moveDir * Time.deltaTime;

        // --- C. HEIGHT SNAP (Stability) ---
        // Calculate the exact Y position on the plane for our current X/Z
        // This keeps us 'hoverHeight' distance away from the surface
        float verticalOffset = hoverHeight / Mathf.Max(0.1f, hit.normal.y);
        
        Vector3 currentPos = transform.position;
        float yOnPlane = hit.point.y - (
            (hit.normal.x * (currentPos.x - hit.point.x) + 
             hit.normal.z * (currentPos.z - hit.point.z)) 
            / hit.normal.y
        );

        // Snap Y position (Only modify Y!)
        transform.position = new Vector3(currentPos.x, yOnPlane + verticalOffset, currentPos.z);

        // --- D. ROTATION ---
        // Align to surface
        Quaternion targetRot = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 15f);
    }

    private void HandleAirborne()
    {
        _isGrounded = false;

        // --- A. ACCELERATION (Falling Logic) ---
        // Apply pure World Gravity downwards
        _velocity += Vector3.down * gravityForce * Time.deltaTime;

        // Apply Air Drag (Much lower than ground drag)
        _velocity = Vector3.Lerp(_velocity, Vector3.zero, airDrag * Time.deltaTime);

        // --- B. MOVEMENT (Free) ---
        // Move freely in 3D space
        transform.position += _velocity * Time.deltaTime;

        // --- C. ROTATION ---
        // Slowly rotate back to upright while falling (optional aesthetics)
        Quaternion uprightRot = Quaternion.FromToRotation(transform.up, Vector3.up) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, uprightRot, Time.deltaTime * 2f);
    }
    
    // Debug visualizer
    private void OnDrawGizmos()
    {
        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.2f);
        Gizmos.DrawLine(transform.position, transform.position + _velocity);
    }
}