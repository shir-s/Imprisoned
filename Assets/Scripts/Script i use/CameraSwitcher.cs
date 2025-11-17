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
        if (cameras == null) return;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null) continue;

            bool isActive = (i == activeIndex);
            cam.gameObject.SetActive(isActive);

            // Keep only one AudioListener enabled
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = isActive;
        }
    }
}