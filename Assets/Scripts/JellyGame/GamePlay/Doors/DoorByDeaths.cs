    // FILEPATH: Assets/Scripts/World/Doors/DoorByDeaths.cs

    using JellyGame.GamePlay.Audio.Core;
    using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.World.Doors
{
    /// <summary>
    /// Simple door that opens after N deaths from specific layers.
    /// Opening = move along a local direction (tilt-aware if parented to the tilting surface).
    /// Optionally switches the door's layer when opened (e.g., from "Wall" to "Default").
    /// </summary>
    [DisallowMultipleComponent]
    public class DoorByDeaths : MonoBehaviour
    {
        [Header("Death Requirement")]
        [Tooltip("Only deaths on these layers will be counted.")]
        [SerializeField] private LayerMask countLayers = ~0;

        [SerializeField] private int requiredDeaths = 3;

        [Header("Open Movement (local space)")]
        [Tooltip("Direction in LOCAL space. Example: (0,1,0) moves 'up' relative to the parent (tilt-aware).")]
        [SerializeField] private Vector3 openLocalDirection = Vector3.up;

        [SerializeField] private float openDistance = 2f;
        [SerializeField] private float openDuration = 0.6f;
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Layer Change")]
        [Tooltip("If enabled, door (and optionally children) will change to this layer AFTER opening.")]
        [SerializeField] private bool changeLayerOnOpen = true;

        [Tooltip("Target layer index to set after open. (Use Unity's Layer dropdown to pick the number.)")]
        [SerializeField] private int openedLayer = 0; // Default

        [Tooltip("If true, apply the layer change to all children too (recommended if colliders are on child objects).")]
        [SerializeField] private bool applyToChildren = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private int _count;
        private bool _opening;
        private bool _opened;

        private Vector3 _closedLocalPos;

        private void Awake()
        {
            _closedLocalPos = transform.localPosition;

            if (requiredDeaths < 1)
                requiredDeaths = 1;

            if (openDuration < 0.01f)
                openDuration = 0.01f;
        }

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.EntityDied, OnEntityDied);
        }

        private void OnEntityDied(object eventData)
        {
            if (_opened || _opening)
                return;

            if (eventData is not EntityDiedEventData e)
                return;

            int layer = e.VictimLayer;

            if ((countLayers.value & (1 << layer)) == 0)
                return;

            _count++;

            if (debugLogs)
                Debug.Log($"[DoorByDeaths] Counted death {_count}/{requiredDeaths} (layer={layer})", this);

            if (_count >= requiredDeaths)
                StartCoroutine(OpenRoutine());
        }

        private System.Collections.IEnumerator OpenRoutine()
        {
            _opening = true;
            
            SoundManager.Instance.PlaySound("DoorOpen", transform);

            Vector3 dir = openLocalDirection;
            if (dir.sqrMagnitude < 1e-6f)
                dir = Vector3.up;

            dir.Normalize();

            Vector3 start = _closedLocalPos;
            Vector3 end = _closedLocalPos + dir * openDistance;

            float t = 0f;
            float d = openDuration;

            while (t < d)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / d);
                float e = ease.Evaluate(u);

                transform.localPosition = Vector3.LerpUnclamped(start, end, e);
                yield return null;
            }

            transform.localPosition = end;

            if (changeLayerOnOpen)
                SetLayerRecursive(gameObject, openedLayer, applyToChildren);

            _opening = false;
            _opened = true;

            if (debugLogs)
                Debug.Log($"[DoorByDeaths] Door opened. Layer changed to {openedLayer}.", this);
        }

        private static void SetLayerRecursive(GameObject root, int layer, bool includeChildren)
        {
            if (root == null)
                return;

            root.layer = layer;

            if (!includeChildren)
                return;

            Transform t = root.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer, true);
        }
    }
}
