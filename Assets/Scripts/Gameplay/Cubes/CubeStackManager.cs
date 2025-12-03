// // FILEPATH: Assets/Scripts/PhysicsDrawing/CubeStackManager.cs
// using System;
// using System.Collections.Generic;
// using UnityEngine;
//
// /// <summary>
// /// Manages a vertical stack of paint cubes:
// /// - The bottom cube is the active "main" cube (physics, wear, painting).
// /// - When you collect pickups, new cubes (prefabs) are spawned and stacked visually on top.
// /// - Only the bottom cube has Rigidbody + collider + wear + painter enabled.
// /// - When the bottom cube shrinks below a threshold, it is DESTROYED
// ///   and the cube above it becomes the new main.
// /// - Upper cubes keep constant size (no scaling) and are re-positioned every frame so
// ///   there is no gap as the bottom shrinks.
// /// 
// /// Assumptions:
// /// - Stacking axis is local +Y of the main cube (cube.up).
// /// - Each cube prefab has a BoxCollider whose size.y is its height.
// /// - WearWhenMovingScaler shrinks along Y (shrinkAxis = Y).
// /// </summary>
// [DisallowMultipleComponent]
// public class CubeStackManager : MonoBehaviour
// {
//     #region Singleton (simple, one stack in scene)
//     public static CubeStackManager Instance { get; private set; }
//
//     void Awake()
//     {
//         if (Instance != null && Instance != this)
//         {
//             Debug.LogWarning("[CubeStackManager] More than one instance in scene, destroying extra.");
//             Destroy(gameObject);
//             return;
//         }
//         Instance = this;
//     }
//     #endregion
//
//     /// <summary>
//     /// Fired whenever the active main cube changes (initial setup and on promotion).
//     /// Argument is the main cube GameObject.
//     /// </summary>
//     public event Action<GameObject> MainCubeChanged;
//
//     [Header("Initial Setup")]
//     [Tooltip("The current cube already in your scene (with Rigidbody, WearWhenMovingScaler, MovementPaintController, etc).")]
//     [SerializeField] private GameObject initialMainCube;
//
//     [Header("Wear Detection")]
//     [Tooltip("When main cube.localScale.y drops below 'initialScaleY * this', we consider it fully worn and destroy it.")]
//     [Range(0.01f, 0.5f)]
//     [SerializeField] private float wornScaleFractionThreshold = 0.1f;
//
//     [Tooltip("Safety minimum for scale Y below which we also treat the cube as worn (even if initial scale is weird).")]
//     [SerializeField] private float absoluteMinScaleY = 0.02f;
//
//     [Header("Debug")]
//     [SerializeField] private bool logEvents = false;
//
//     // Internal representation of a cube in the stack
//     private class CubeEntry
//     {
//         public GameObject go;
//         public Transform tr;
//         public BoxCollider box;
//         public Rigidbody rb;
//         public Collider col;
//         public WearWhenMovingScaler wear;
//         public MovementPaintController paint;
//         public ExtraDownForce extraGravity;
//
//         public float initialLocalScaleY;
//         public bool isMain;
//     }
//
//     // Bottom is index 0, top is last
//     private readonly List<CubeEntry> _stack = new List<CubeEntry>();
//
//     void Start()
//     {
//         if (initialMainCube == null)
//         {
//             Debug.LogError("[CubeStackManager] Initial main cube is not assigned.");
//             enabled = false;
//             return;
//         }
//
//         // Register the existing cube as the first (bottom) main cube
//         CubeEntry main = RegisterCube(initialMainCube, isMain: true);
//         if (main == null)
//         {
//             enabled = false;
//             return;
//         }
//         _stack.Add(main);
//
//         if (logEvents)
//             Debug.Log("[CubeStackManager] Registered initial main cube.");
//
//         RaiseMainCubeChanged(initialMainCube);
//     }
//
//     void Update()
//     {
//         if (_stack.Count == 0)
//             return;
//
//         UpdateStackVisuals();
//         CheckWearOfMain();
//     }
//
//     /// <summary>
//     /// Public accessor for the current main cube transform (useful if something else needs to know).
//     /// </summary>
//     public Transform CurrentMainTransform =>
//         _stack.Count > 0 ? _stack[0].tr : null;
//
//     #region Public API (called from pickups)
//
//     /// <summary>
//     /// Called by CubePickup when the main cube collects a pickup.
//     /// Spawns a new cube from prefab and adds it on top of the stack.
//     /// </summary>
//     public void AddCubeFromPickup(GameObject cubePrefab)
//     {
//         if (cubePrefab == null)
//         {
//             Debug.LogWarning("[CubeStackManager] Pickup tried to give null prefab.");
//             return;
//         }
//
//         if (_stack.Count == 0)
//         {
//             Debug.LogWarning("[CubeStackManager] No main cube yet, cannot add to stack.");
//             return;
//         }
//
//         // Spawn near the main cube; we'll move it to the correct stacked position in UpdateStackVisuals.
//         Transform main = _stack[0].tr;
//         GameObject newCubeGO = Instantiate(cubePrefab, main.position, main.rotation);
//
//         CubeEntry entry = RegisterCube(newCubeGO, isMain: false);
//         if (entry == null)
//         {
//             Destroy(newCubeGO);
//             return;
//         }
//
//         _stack.Add(entry);
//
//         if (logEvents)
//             Debug.Log($"[CubeStackManager] Added cube '{newCubeGO.name}' to stack. New count = {_stack.Count}");
//     }
//
//     #endregion
//
//     #region Core logic
//
//     private CubeEntry RegisterCube(GameObject go, bool isMain)
//     {
//         if (go == null) return null;
//
//         CubeEntry c = new CubeEntry();
//         c.go          = go;
//         c.tr          = go.transform;
//         c.box         = go.GetComponent<BoxCollider>();
//         c.rb          = go.GetComponent<Rigidbody>();
//         c.col         = go.GetComponent<Collider>();
//         c.wear        = go.GetComponent<WearWhenMovingScaler>();
//         c.paint       = go.GetComponent<MovementPaintController>();
//         c.extraGravity= go.GetComponent<ExtraDownForce>();
//         c.initialLocalScaleY = c.tr.localScale.y;
//         c.isMain      = isMain;
//
//         if (c.box == null)
//         {
//             Debug.LogError($"[CubeStackManager] Cube '{go.name}' has no BoxCollider. Cannot compute height.");
//             return null;
//         }
//
//         /*// For non-main cubes: disable physics + painting + wear,
//         // they are just visual until they become main.
//         if (!isMain)
//         {
//             if (c.rb != null)
//             {
//                 c.rb.isKinematic = true;
//                 c.rb.useGravity = false;
//                 c.rb.linearVelocity = Vector3.zero;
//                 c.rb.angularVelocity = Vector3.zero;
//             }
//
//             if (c.col != null)
//                 c.col.enabled = false;
//
//             if (c.paint != null)
//                 c.paint.enabled = false;
//
//             if (c.wear != null)
//                 c.wear.enabled = false;
//
//             if (c.extraGravity != null)
//                 c.extraGravity.enabled = false;
//         }
//         else
//         {
//             // Ensure main has physics and wear active
//             if (c.rb != null)
//             {
//                 c.rb.isKinematic = false;
//                 c.rb.useGravity = true;
//             }
//
//             if (c.col != null)
//                 c.col.enabled = true;
//
//             if (c.wear != null)
//                 c.wear.enabled = true;
//
//             if (c.paint != null)
//                 c.paint.enabled = true;
//
//             if (c.extraGravity != null)
//                 c.extraGravity.enabled = true;
//         }*/
//         
//         if (c.rb != null)
//         {
//             c.rb.isKinematic = true;
//             c.rb.useGravity = false;
//         }
//         
//         if (c.col != null)
//             c.col.enabled = isMain;
//
//         if (c.paint != null)
//             c.paint.enabled = isMain;
//
//         if (c.wear != null)
//             c.wear.enabled = isMain;
//
//         if (c.extraGravity != null)
//             c.extraGravity.enabled = false;
//
//         return c;
//     }
//
//     private void UpdateStackVisuals()
//     {
//         // Only need to reposition upper cubes if we have 2 or more
//         if (_stack.Count < 2)
//             return;
//
//         CubeEntry main = _stack[0];
//         Transform mainTr = main.tr;
//
//         // Stacking axis = main cube's local +Y in world space
//         Vector3 axis = mainTr.up;
//
//         // Start at top of bottom cube
//         float mainHalfHeight = GetHalfHeight(main);
//         Vector3 currTop = mainTr.position + axis * mainHalfHeight;
//
//         // All upper cubes follow main's rotation and are placed on top of each other
//         for (int i = 1; i < _stack.Count; i++)
//         {
//             CubeEntry c = _stack[i];
//
//             // Rotation: follow main
//             c.tr.rotation = mainTr.rotation;
//
//             float half = GetHalfHeight(c);
//             Vector3 newPos = currTop + axis * half;
//
//             c.tr.position = newPos;
//
//             // Top of this cube = base for next
//             currTop = c.tr.position + axis * half;
//         }
//     }
//
//     private float GetHalfHeight(CubeEntry c)
//     {
//         if (c.box == null) return 0.0f;
//
//         // BoxCollider size is in local space; height is size.y.
//         // World height = local size * localScale.y
//         float heightWorld = c.box.size.y * Mathf.Abs(c.tr.localScale.y);
//         return 0.5f * heightWorld;
//     }
//
//     private void CheckWearOfMain()
//     {
//         if (_stack.Count == 0) return;
//
//         CubeEntry main = _stack[0];
//         if (main.wear == null)
//             return;
//
//         float currentY = main.tr.localScale.y;
//         float initialY = Mathf.Max(0.0001f, main.initialLocalScaleY);
//
//         bool belowFraction = currentY <= initialY * wornScaleFractionThreshold;
//         bool belowAbsolute = currentY <= absoluteMinScaleY;
//
//         if (belowFraction || belowAbsolute)
//         {
//             // Fully worn -> destroy and promote
//             if (logEvents)
//                 Debug.Log($"[CubeStackManager] Main cube '{main.go.name}' worn out. Promoting next.");
//
//             HandleMainCubeWornOut();
//         }
//     }
//
//     private void HandleMainCubeWornOut()
//     {
//         if (_stack.Count == 0)
//             return;
//
//         CubeEntry oldMain = _stack[0];
//         _stack.RemoveAt(0);
//
//         // Destroy the old cube GameObject (this is the "bottom cube destroyed" effect)
//         if (oldMain.go != null)
//         {
//             Destroy(oldMain.go);
//         }
//
//         if (_stack.Count == 0)
//         {
//             if (logEvents)
//                 Debug.Log("[CubeStackManager] Stack is now empty. No more cubes to paint with.");
//             return;
//         }
//
//         // Promote the new bottom cube
//         CubeEntry newMain = _stack[0];
//         newMain.isMain = true;
//
//         if (newMain.rb != null)
//         {
//             newMain.rb.isKinematic = false;
//             newMain.rb.useGravity = true;
//             newMain.rb.linearVelocity = Vector3.zero;
//             newMain.rb.angularVelocity = Vector3.zero;
//         }
//
//         if (newMain.col != null)
//             newMain.col.enabled = true;
//
//         if (newMain.paint != null)
//             newMain.paint.enabled = true;
//
//         if (newMain.wear != null)
//         {
//             // Reset its initial scale reference for future wear check
//             newMain.initialLocalScaleY = newMain.tr.localScale.y;
//             newMain.wear.enabled = true;
//         }
//
//         if (newMain.extraGravity != null)
//             newMain.extraGravity.enabled = true;
//
//         if (logEvents)
//             Debug.Log($"[CubeStackManager] New main cube: '{newMain.go.name}'. Remaining in stack: {_stack.Count}");
//
//         RaiseMainCubeChanged(newMain.go);
//     }
//
//     private void RaiseMainCubeChanged(GameObject go)
//     {
//         if (go == null)
//             return;
//
//         if (logEvents)
//             Debug.Log($"[CubeStackManager] Main cube changed to '{go.name}'");
//
//         MainCubeChanged?.Invoke(go);
//     }
//
//     #endregion
// }
// FILEPATH: Assets/Scripts/PhysicsDrawing/CubeStackManager.cs

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NEW BEHAVIOR:
/// - Only one "main" cube actually paints and wears down.
/// - When the player collects a pickup cube, the main cube GROWS
///   (like "eating" paint) instead of stacking visual cubes on top.
/// - WearWhenMovingScaler still shrinks the cube as it moves/paints.
/// - When the cube becomes too small, it can be considered "empty"
///   (current logic still destroys it if you keep CheckWearOfMain).
/// </summary>
[DisallowMultipleComponent]
public class CubeStackManager : MonoBehaviour
{
    #region Singleton (simple, one stack in scene)
    public static CubeStackManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CubeStackManager] More than one instance in scene, destroying extra.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    #endregion

    /// <summary>
    /// Fired whenever the active main cube changes (initial setup and on promotion).
    /// In the new design we only have one main cube, but we keep the event for compatibility.
    /// </summary>
    public event Action<GameObject> MainCubeChanged;

    [Header("Initial Setup")]
    [Tooltip("The current cube already in your scene (with Rigidbody, WearWhenMovingScaler, MovementPaintController, etc).")]
    [SerializeField] private GameObject initialMainCube;

    [Header("Wear Detection")]
    [Tooltip("When main cube.localScale.y drops below 'initialScaleY * this', we consider it fully worn and destroy it.")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float wornScaleFractionThreshold = 0.1f;

    [Tooltip("Safety minimum for scale Y below which we also treat the cube as worn (even if initial scale is weird).")]
    [SerializeField] private float absoluteMinScaleY = 0.02f;

    [Header("Grow-on-pickup (NEW mechanic)")]
    [Tooltip("How much to increase the cube scale per pickup (in local scale units).")]
    [SerializeField] private float growAmountPerPickup = 0.15f;

    [Tooltip("If true, grow uniformly in X,Y,Z (cube gets fatter in all directions).\n" +
             "If false, only grow along Y (height).")]
    [SerializeField] private bool uniformGrow = true;

    [Tooltip("Maximum local scale the cube can reach (approx, used as clamp).")]
    [SerializeField] private float maxScale = 3.0f;

    [Header("Debug")]
    [SerializeField] private bool logEvents = false;

    // Internal representation of a cube (we still reuse the same structure,
    // but in the new design we only ever keep ONE entry in the list).
    private class CubeEntry
    {
        public GameObject go;
        public Transform tr;
        public BoxCollider box;
        public Rigidbody rb;
        public Collider col;
        public WearWhenMovingScaler wear;
        public MovementPaintController paint;
        public ExtraDownForce extraGravity;

        public float initialLocalScaleY;
        public bool isMain;
    }

    // In the new design, index 0 is the ONLY real cube.
    private readonly List<CubeEntry> _stack = new List<CubeEntry>();

    void Start()
    {
        if (initialMainCube == null)
        {
            Debug.LogError("[CubeStackManager] Initial main cube is not assigned.");
            enabled = false;
            return;
        }

        // Register the existing cube as the first (and only) main cube
        CubeEntry main = RegisterCube(initialMainCube, isMain: true);
        if (main == null)
        {
            enabled = false;
            return;
        }
        _stack.Add(main);

        if (logEvents)
            Debug.Log("[CubeStackManager] Registered initial main cube.");

        RaiseMainCubeChanged(initialMainCube);
    }

    void Update()
    {
        if (_stack.Count == 0)
            return;

        // We no longer use visual stacking, but we keep wear check if you still
        // want the cube to disappear when fully worn.
        CheckWearOfMain();
    }

    /// <summary>
    /// Public accessor for the current main cube transform (useful if something else needs to know).
    /// </summary>
    public Transform CurrentMainTransform =>
        _stack.Count > 0 ? _stack[0].tr : null;

    #region Public API (called from pickups)

    /// <summary>
    /// Called by CubePickup when the main cube collects a pickup.
    /// OLD: spawned a new cube prefab and stacked it.
    /// NEW: simply grows the main cube (like "eating" the pickup).
    /// </summary>
    public void AddCubeFromPickup(GameObject cubePrefab)
    {
        if (_stack.Count == 0)
        {
            Debug.LogWarning("[CubeStackManager] No main cube yet, cannot grow.");
            return;
        }

        CubeEntry main = _stack[0];
        GrowMainCube(main);
    }

    #endregion

    #region Core logic

    private CubeEntry RegisterCube(GameObject go, bool isMain)
    {
        if (go == null) return null;

        CubeEntry c = new CubeEntry();
        c.go          = go;
        c.tr          = go.transform;
        c.box         = go.GetComponent<BoxCollider>();
        c.rb          = go.GetComponent<Rigidbody>();
        c.col         = go.GetComponent<Collider>();
        c.wear        = go.GetComponent<WearWhenMovingScaler>();
        c.paint       = go.GetComponent<MovementPaintController>();
        c.extraGravity= go.GetComponent<ExtraDownForce>();
        c.initialLocalScaleY = c.tr.localScale.y;
        c.isMain      = isMain;

        if (c.box == null)
        {
            Debug.LogError($"[CubeStackManager] Cube '{go.name}' has no BoxCollider. Cannot compute height.");
            return null;
        }

        // Everything is kinematic in your project.
        if (c.rb != null)
        {
            c.rb.isKinematic = true;
            c.rb.useGravity = false;
        }

        if (c.col != null)
            c.col.enabled = isMain;

        if (c.paint != null)
            c.paint.enabled = isMain;

        if (c.wear != null)
            c.wear.enabled = isMain;

        if (c.extraGravity != null)
            c.extraGravity.enabled = false;

        return c;
    }

    /// <summary>
    /// NEW: grow the main cube when a pickup is collected.
    /// </summary>
    private void GrowMainCube(CubeEntry main)
    {
        if (main == null || main.tr == null)
            return;

        Vector3 s = main.tr.localScale;

        if (uniformGrow)
        {
            float currentSize = s.x;
            float newSize = Mathf.Min(maxScale, currentSize + growAmountPerPickup);

            s = new Vector3(newSize, newSize, newSize);
        }
        else
        {
            float newY = Mathf.Min(maxScale, s.y + growAmountPerPickup);
            s.y = newY;
        }
        main.tr.localScale = s;
        main.initialLocalScaleY = s.y;
        
        var controller = main.go.GetComponent<CubePlayerKeyboardController>();
        if (controller != null)
        {
            float sizeFactor = uniformGrow ? s.x : s.y;
            controller.SetSizeFactor(sizeFactor);
        }

        if (logEvents)
            Debug.Log($"[CubeStackManager] Grew main cube → new scale = {main.tr.localScale}");
    }

    private void CheckWearOfMain()
    {
        if (_stack.Count == 0) return;

        CubeEntry main = _stack[0];
        if (main.wear == null)
            return;

        float currentY = main.tr.localScale.y;
        float initialY = Mathf.Max(0.0001f, main.initialLocalScaleY);

        bool belowFraction = currentY <= initialY * wornScaleFractionThreshold;
        bool belowAbsolute = currentY <= absoluteMinScaleY;

        if (belowFraction || belowAbsolute)
        {
            // Fully worn -> destroy and "no more paint".
            if (logEvents)
                Debug.Log($"[CubeStackManager] Main cube '{main.go.name}' worn out. No more cubes to promote.");

            HandleMainCubeWornOut();
        }
    }

    private void HandleMainCubeWornOut()
    {
        if (_stack.Count == 0)
            return;

        CubeEntry oldMain = _stack[0];
        _stack.RemoveAt(0);

        // Destroy the old cube GameObject
        if (oldMain.go != null)
        {
            Destroy(oldMain.go);
        }

        if (_stack.Count == 0)
        {
            if (logEvents)
                Debug.Log("[CubeStackManager] Stack is now empty. No more cubes to paint with.");
            // Here you can trigger Game Over UI, restart, etc.
            return;
        }

        // In the new mechanic we never actually get here (because we never push more cubes),
        // but we keep this code for compatibility if you ever extend the design.
        CubeEntry newMain = _stack[0];
        newMain.isMain = true;

        if (newMain.col != null)
            newMain.col.enabled = true;

        if (newMain.paint != null)
            newMain.paint.enabled = true;

        if (newMain.wear != null)
        {
            newMain.initialLocalScaleY = newMain.tr.localScale.y;
            newMain.wear.enabled = true;
        }

        if (logEvents)
            Debug.Log($"[CubeStackManager] New main cube: '{newMain.go.name}'. Remaining in stack: {_stack.Count}");

        RaiseMainCubeChanged(newMain.go);
    }

    private void RaiseMainCubeChanged(GameObject go)
    {
        if (go == null)
            return;

        if (logEvents)
            Debug.Log($"[CubeStackManager] Main cube changed to '{go.name}'");

        MainCubeChanged?.Invoke(go);
    }

    #endregion
}
