// FILEPATH: Assets/Scripts/Camera/CameraSwitcher.cs
using UnityEngine;

[DisallowMultipleComponent]
public class CameraSwitcher : MonoBehaviour
{
    [Tooltip("List of cameras to toggle between.")]
    [SerializeField] private Camera[] cameras;

    [Tooltip("Index of the camera that starts active (0-based).")]
    [SerializeField] private int activeIndex = 0;

    [Tooltip("Key used to switch cameras.")]
    [SerializeField] private KeyCode switchKey = KeyCode.Space;

    [Header("Tray / Input Binding")]
    [Tooltip("Tray that is controlled by the arrow keys.")]
    [SerializeField] private TiltTray tiltTray;

    [Tooltip("Index of the FOLLOW camera in the list (camera-relative controls).")]
    [SerializeField] private int followCameraIndex = 1;

    [Tooltip("If true, when the follow camera is active, tray input is relative to that camera's right/forward.")]
    [SerializeField] private bool useCameraRelativeInputForFollow = true;

    private void Start()
    {
        ApplyActiveCamera();
    }

    private void Update()
    {
        if (Input.GetKeyDown(switchKey))
        {
            if (cameras == null || cameras.Length == 0)
                return;

            activeIndex++;
            if (activeIndex >= cameras.Length)
                activeIndex = 0;

            ApplyActiveCamera();
        }
    }

    private void ApplyActiveCamera()
    {
        if (cameras == null || cameras.Length == 0)
            return;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) 
                continue;

            bool isActive = (i == activeIndex);
            cam.gameObject.SetActive(isActive);

            // Keep only one AudioListener enabled
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = isActive;
        }

        // Bind tray input to the currently active camera (or world) as needed
        if (tiltTray != null)
        {
            bool followModeActive = 
                useCameraRelativeInputForFollow &&
                followCameraIndex >= 0 &&
                followCameraIndex < cameras.Length &&
                activeIndex == followCameraIndex &&
                cameras[followCameraIndex] != null;

            if (followModeActive)
            {
                // Follow camera active: arrow keys work relative to that camera
                tiltTray.SetInputCamera(cameras[followCameraIndex].transform, true);
            }
            else
            {
                // Top view (or any non-follow camera): world-relative controls
                tiltTray.SetInputCamera(null, false);
            }
        }
    }
}
