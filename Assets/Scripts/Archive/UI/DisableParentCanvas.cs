// FILEPATH: Assets/Scripts/UI/DisableParentCanvas.cs
using UnityEngine;

public class DisableParentCanvas : MonoBehaviour
{
    public void DisableCanvas()
    {
        Canvas c = GetComponentInParent<Canvas>();

        if (c != null)
            c.gameObject.SetActive(false);
    }
}
