using System.Collections.Generic;
using UnityEngine;
namespace Toy2
{
    public class DualStretchQueueController : MonoBehaviour
    {
        [Header("X Queue (stretch right)")]
        public KeyCode xKey = KeyCode.RightArrow;
        public float xGrowSpeed = 2f;
        [Tooltip("Stretchables that grow along X, in the order they should complete")]
        public List<StretchableAxis> xItems = new List<StretchableAxis>();

        [Header("Z Queue (stretch forward/up in top-down)")]
        public KeyCode zKey = KeyCode.UpArrow;
        public float zGrowSpeed = 2f;
        [Tooltip("Stretchables that grow along Z, in the order they should complete")]
        public List<StretchableAxis> zItems = new List<StretchableAxis>();

        int xIndex = 0;
        int zIndex = 0;

        void Start()
        {
            // Optionally validate axis settings
            ValidateAxes(xItems, StretchableAxis.Axis.X);
            ValidateAxes(zItems, StretchableAxis.Axis.Z);

            AdvanceToNextIncompleteX();
            AdvanceToNextIncompleteZ();
        }

        void Update()
        {
            // X queue handling (Right Arrow)
            if (xIndex < xItems.Count && Input.GetKey(xKey))
            {
                var activeX = xItems[xIndex];
                if (activeX != null && activeX.isActiveAndEnabled)
                {
                    activeX.StretchStep(xGrowSpeed * Time.deltaTime);
                    if (activeX.IsComplete) AdvanceToNextIncompleteX();
                }
                else AdvanceToNextIncompleteX();
            }

            // Z queue handling (Up Arrow)
            if (zIndex < zItems.Count && Input.GetKey(zKey))
            {
                var activeZ = zItems[zIndex];
                if (activeZ != null && activeZ.isActiveAndEnabled)
                {
                    activeZ.StretchStep(zGrowSpeed * Time.deltaTime);
                    if (activeZ.IsComplete) AdvanceToNextIncompleteZ();
                }
                else AdvanceToNextIncompleteZ();
            }
        }

        void AdvanceToNextIncompleteX()
        {
            while (xIndex < xItems.Count)
            {
                var it = xItems[xIndex];
                if (it != null && it.isActiveAndEnabled && !it.IsComplete) break;
                xIndex++;
            }
        }

        void AdvanceToNextIncompleteZ()
        {
            while (zIndex < zItems.Count)
            {
                var it = zItems[zIndex];
                if (it != null && it.isActiveAndEnabled && !it.IsComplete) break;
                zIndex++;
            }
        }

        void ValidateAxes(List<StretchableAxis> list, StretchableAxis.Axis expected)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) continue;
                if (it.axis != expected)
                {
                    Debug.LogWarning($"DualStretchQueueController: Item at index {i} on {(expected==StretchableAxis.Axis.X?"X":"Z")} queue has axis {it.axis}. Consider switching it to {expected}.");
                }
            }
        }
    }

}