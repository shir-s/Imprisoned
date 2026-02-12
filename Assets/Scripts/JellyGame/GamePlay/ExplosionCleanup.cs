// FILEPATH: Assets/Scripts/JellyGame/GamePlay/World/ExplosionCleanup.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.World
{
    /// <summary>
    /// When the Explosion event fires, cleans up the scene:
    /// 1. Destroys all objects on specified layers
    /// 2. Destroys specific GameObjects from a list
    /// 3. Disables specific scripts/behaviours from a list
    /// </summary>
    [DisallowMultipleComponent]
    public class ExplosionCleanup : MonoBehaviour
    {
        [Header("Destroy By Layer")]
        [Tooltip("All active GameObjects on these layers will be destroyed.")]
        [SerializeField] private LayerMask destroyLayers;

        [Header("Destroy Specific Objects")]
        [Tooltip("These specific GameObjects will be destroyed.")]
        [SerializeField] private List<GameObject> destroyObjects = new List<GameObject>();

        [Header("Disable Scripts")]
        [Tooltip("These scripts/behaviours will be disabled (not destroyed).")]
        [SerializeField] private List<Behaviour> disableScripts = new List<Behaviour>();

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private void OnEnable()
        {
            Managers.EventManager.StartListening(Managers.EventManager.GameEvent.Explosion, OnExplosion);
        }

        private void OnDisable()
        {
            Managers.EventManager.StopListening(Managers.EventManager.GameEvent.Explosion, OnExplosion);
        }

        private void OnExplosion(object _)
        {
            DestroyByLayers();
            DestroySpecificObjects();
            DisableScripts();
        }

        private void DestroyByLayers()
        {
            if (destroyLayers.value == 0)
                return;

            // FindObjectsOfType<Transform> gets every active object in the scene
            Transform[] all = FindObjectsOfType<Transform>();
            int count = 0;

            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i].gameObject;
                if (go == null) continue;

                if ((destroyLayers.value & (1 << go.layer)) != 0)
                {
                    if (debugLogs)
                        Debug.Log($"[ExplosionCleanup] Destroying '{go.name}' (layer '{LayerMask.LayerToName(go.layer)}')", this);

                    Destroy(go);
                    count++;
                }
            }

            if (debugLogs)
                Debug.Log($"[ExplosionCleanup] Destroyed {count} object(s) by layer.", this);
        }

        private void DestroySpecificObjects()
        {
            if (destroyObjects == null || destroyObjects.Count == 0)
                return;

            for (int i = 0; i < destroyObjects.Count; i++)
            {
                if (destroyObjects[i] == null) continue;

                if (debugLogs)
                    Debug.Log($"[ExplosionCleanup] Destroying '{destroyObjects[i].name}'", this);

                Destroy(destroyObjects[i]);
            }
        }

        private void DisableScripts()
        {
            if (disableScripts == null || disableScripts.Count == 0)
                return;

            for (int i = 0; i < disableScripts.Count; i++)
            {
                if (disableScripts[i] == null) continue;

                if (debugLogs)
                    Debug.Log($"[ExplosionCleanup] Disabling '{disableScripts[i].GetType().Name}' on '{disableScripts[i].gameObject.name}'", this);

                disableScripts[i].enabled = false;
            }
        }
    }
}