// FILEPATH: Assets/Scripts/UI/CanvasActivator.cs
using UnityEngine;

namespace JellyGame.UI
{
    public class CanvasActivator : MonoBehaviour
    {
        [SerializeField] private Canvas targetCanvas;

        /// <summary>
        /// Call this from an Animation Event to activate the assigned canvas.
        /// </summary>
        public void ActivateCanvas()
        {
            if (targetCanvas != null)
                targetCanvas.gameObject.SetActive(true);
        }
    }
}