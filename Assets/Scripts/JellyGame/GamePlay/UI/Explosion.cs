// FILEPATH: Assets/Scripts/JellyGame/GamePlay/UI/Explosion.cs
using System;
using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.UI
{
    public class Explosion : MonoBehaviour
    {
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float effectDuration = 20f;
        [SerializeField] private GameObject[] chainsToExplode;
        [SerializeField] private GameObject chainFall;

        [Header("Chain Fall Delay")]
        [Tooltip("Seconds to wait after the explosion before activating the chain fall.")]
        [SerializeField] private float chainFallDelay = 1f;

        [Header("Activate After Chain Fall")]
        [Tooltip("GameObjects to activate after the chain fall finishes (e.g. FinishTrigger portal).")]
        [SerializeField] private List<GameObject> activateAfterChainFall = new List<GameObject>();

        [Tooltip("Seconds to wait after chain fall activates before enabling these objects/scripts.")]
        [SerializeField] private float activateAfterChainFallDelay = 3f;

        [Tooltip("Scripts to enable after the chain fall delay (e.g. movement scripts, abilities).")]
        [SerializeField] private List<Behaviour> enableAfterChainFall = new List<Behaviour>();

        private void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.Explosion, OnExplosion);
        }

        private void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.Explosion, OnExplosion);
        }

        private void Awake()
        {
            if (explosionEffect == null)
                explosionEffect = GameObject.FindGameObjectWithTag("Explosion");

            if (chainsToExplode == null || chainsToExplode.Length == 0)
                chainsToExplode = GameObject.FindGameObjectsWithTag("Chains");

            if (chainFall == null)
                chainFall = GameObject.FindGameObjectWithTag("ChainFall");
        }

        private void OnExplosion(object eventdata)
        {
            if (explosionEffect != null)
            {
                explosionEffect.SetActive(true);

                for (int i = 0; i < chainsToExplode.Length; i++)
                {
                    if (chainsToExplode[i] != null)
                        chainsToExplode[i].SetActive(false);
                }

                StartCoroutine(DeactivateExplosionEffect(effectDuration));
                StartCoroutine(ActivateChainFallAfterDelay());
            }
        }

        private IEnumerator ActivateChainFallAfterDelay()
        {
            if (chainFallDelay > 0f)
                yield return new WaitForSeconds(chainFallDelay);

            if (chainFall != null)
            {
                chainFall.SetActive(true);
                StartCoroutine(DeactivateChainFall(5f));
            }

            if (activateAfterChainFallDelay > 0f)
                yield return new WaitForSeconds(activateAfterChainFallDelay);

            if (activateAfterChainFall != null)
            {
                for (int i = 0; i < activateAfterChainFall.Count; i++)
                {
                    if (activateAfterChainFall[i] != null)
                        activateAfterChainFall[i].SetActive(true);
                }
            }

            if (enableAfterChainFall != null)
            {
                for (int i = 0; i < enableAfterChainFall.Count; i++)
                {
                    if (enableAfterChainFall[i] != null)
                        enableAfterChainFall[i].enabled = true;
                }
            }

            EventManager.TriggerEvent(EventManager.GameEvent.PortalLvl3, null);
        }

        private IEnumerator DeactivateExplosionEffect(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (explosionEffect != null)
                explosionEffect.SetActive(false);
        }

        private IEnumerator DeactivateChainFall(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (chainFall != null)
                chainFall.SetActive(false);
        }
    }
}