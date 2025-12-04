// FILEPATH: Assets/Scripts/Gameplay/WinZone.cs
using UnityEngine;

[DisallowMultipleComponent]
public class WinZone : MonoBehaviour
{
    [Tooltip("Tag of the enemy that should trigger the win event.")]
    [SerializeField] private string enemyTag = "Enemy";

    [Tooltip("If true, destroy the enemy when it enters.")]
    [SerializeField] private bool destroyEnemyOnWin = false;

    [Tooltip("Debug logs")]
    [SerializeField] private bool debugLogs = false;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(enemyTag))
            return;

        if (debugLogs)
            Debug.Log("[WinZone] Enemy entered win zone → WIN!", this);

        // Fire global event
        EventManager.TriggerEvent(EventManager.GameEvent.GameWin, other.gameObject);

        if (destroyEnemyOnWin)
        {
            Destroy(other.gameObject);
        }
    }
}