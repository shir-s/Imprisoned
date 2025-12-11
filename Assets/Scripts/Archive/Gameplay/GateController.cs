// FILEPATH: Assets/Scripts/Gameplay/GateController.cs

using JellyGame.GamePlay.Managers;
using UnityEngine;

[DisallowMultipleComponent]
public class GateController : MonoBehaviour
{
    [Header("Gate Visuals & Collision")]
    [Tooltip("Collider that blocks the player when the gate is closed. If null, will try to use a collider on this GameObject.")]
    [SerializeField] private Collider gateCollider;

    [Tooltip("Renderers that represent the gate mesh. If empty, will use all Renderers under this GameObject.")]
    [SerializeField] private Renderer[] gateRenderers;

    [Tooltip("Should the gate start opened at the beginning of the scene?")]
    [SerializeField] private bool startOpened = false;

    [Header("Layer Switching")]
    [Tooltip("Layer name to switch to when the gate is OPEN.")]
    [SerializeField] private string openedGateLayer = "OpenedGate";

    [Tooltip("Layer name to switch to when the gate is CLOSED.")]
    [SerializeField] private string closedGateLayer = "ClosedGate";

    [Tooltip("Also change layers of children.")]
    [SerializeField] private bool applyLayerToChildren = true;

    [Header("Key Logic")]
    [SerializeField] private EventManager.GameEvent keyEvent = EventManager.GameEvent.KeyCollected;

    [Header("Friendly NPC Death Logic")]
    [Tooltip("If true, the gate also listens to FriendlyNpcKilled and can be blocked by too many deaths.")]
    [SerializeField] private bool listenToFriendlyDeaths = false;

    [Tooltip("Maximum number of friendly NPC deaths allowed before the gate is blocked/closed.")]
    [SerializeField] private int maxFriendlyDeathsAllowed = 1;

    private int _friendlyDeathsCount = 0;
    private bool _hasKeyEventReceived = false;
    private bool _isOpen = false;
    private bool _isBlockedByDeaths = false;

    private void Awake()
    {
        if (gateCollider == null)
            gateCollider = GetComponent<Collider>();

        if (gateRenderers == null || gateRenderers.Length == 0)
            gateRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private void OnEnable()
    {
        EventManager.StartListening(keyEvent, OnKeyEvent);

        if (listenToFriendlyDeaths)
        {
            EventManager.StartListening(EventManager.GameEvent.FriendlyNpcKilled, OnFriendlyNpcKilled);
        }

        // Apply initial state
        if (startOpened)
        {
            _isOpen = true;
            ApplyGateState();
        }
        else
        {
            _isOpen = false;
            ApplyGateState();
        }
    }

    private void OnDisable()
    {
        EventManager.StopListening(keyEvent, OnKeyEvent);

        if (listenToFriendlyDeaths)
        {
            EventManager.StopListening(EventManager.GameEvent.FriendlyNpcKilled, OnFriendlyNpcKilled);
        }
    }

    // --------------------------------------------------------------------
    // Event handlers
    // --------------------------------------------------------------------

    private void OnKeyEvent(object data)
    {
        _hasKeyEventReceived = true;

        if (_isBlockedByDeaths)
            return;

        OpenGate();
    }

    private void OnFriendlyNpcKilled(object data)
    {
        _friendlyDeathsCount++;

        if (!_isBlockedByDeaths && _friendlyDeathsCount >= maxFriendlyDeathsAllowed)
        {
            _isBlockedByDeaths = true;

            if (_isOpen)
                CloseGate();
        }
    }

    // --------------------------------------------------------------------
    // Gate state
    // --------------------------------------------------------------------

    private void OpenGate()
    {
        if (_isOpen)
            return;

        _isOpen = true;
        ApplyGateState();
    }

    private void CloseGate()
    {
        if (!_isOpen)
            return;

        _isOpen = false;
        ApplyGateState();
    }

    private void ApplyGateState()
    {
        bool colliderEnabled = !_isOpen;
        bool rendererEnabled = !_isOpen;

        if (gateCollider != null)
            gateCollider.enabled = colliderEnabled;

        if (gateRenderers != null)
        {
            foreach (var r in gateRenderers)
            {
                if (r != null)
                    r.enabled = rendererEnabled;
            }
        }

        // --- LAYER SWITCHING ---
        if (_isOpen)
            SetLayerRecursively(openedGateLayer);
        else
            SetLayerRecursively(closedGateLayer);
    }

    private void SetLayerRecursively(string layerName)
    {
        int layerID = LayerMask.NameToLayer(layerName);
        if (layerID < 0)
        {
            Debug.LogWarning($"[GateController] Layer '{layerName}' does not exist.");
            return;
        }

        if (applyLayerToChildren)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layerID;
        }
        else
        {
            gameObject.layer = layerID;
        }
    }

    // Debug info
    public bool IsOpen => _isOpen;
    public bool IsBlockedByDeaths => _isBlockedByDeaths;
    public int FriendlyDeathsCount => _friendlyDeathsCount;
}
