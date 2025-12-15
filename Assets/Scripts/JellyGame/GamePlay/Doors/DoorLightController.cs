using System;
using System.Collections.Generic;
using JellyGame.GamePlay.Managers;
using NUnit.Framework;
using UnityEngine;
namespace JellyGame.GamePlay.Doors
{
    public class DoorLightController : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material litMaterial;  
        [SerializeField] private Material unlitMaterial;
        
        private List<GameObject> _lights = new List<GameObject>();
        //private int _doorLightLayer = LayerMask.NameToLayer("DoorLight");
        private int _doorLightLayer = -1;
        private int _deathCounter = 0;

        public void Awake()
        {
            _doorLightLayer = LayerMask.NameToLayer("DoorLight");
            Debug.Assert(_doorLightLayer != -1, "DoorLight layer not found! Check Tags & Layers.");
            
            _lights.Clear();
            
            /*foreach (Transform child in GetComponentsInChildren<Transform>())
            {
                if (child == transform) continue;

                if (child.gameObject.layer == _doorLightLayer)
                {
                    _lights.Add(child.gameObject);
                }
            }*/
            
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;

                if (t.gameObject.layer == _doorLightLayer)
                    _lights.Add(t.gameObject);
            }

            Debug.Log($"[{name}] collected {_lights.Count} DoorLight objects:");
            for (int i = 0; i < _lights.Count; i++)
                Debug.Log($"{i}: {_lights[i].name} layer={LayerMask.LayerToName(_lights[i].layer)}");
            
            if (unlitMaterial != null)
            {
                foreach (var go in _lights)
                {
                    var r = go.GetComponent<Renderer>();
                    if (r != null) r.material = unlitMaterial;
                }
            }
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
            if (eventData is not EntityDiedEventData e)
                return;
            
            if (_deathCounter < 0 || _deathCounter >= _lights.Count)
                return;
            
            var nextLight = _lights[_deathCounter];
            if (nextLight == null)
            {
                _deathCounter++;
                return;
            }
            
            var renderer = nextLight.GetComponent<Renderer>();
            if (renderer != null && litMaterial != null)
            {
                renderer.material = litMaterial;
            }
            
            _deathCounter++;
            
        }
        
    }
}