using System;
using System.Collections;
using JellyGame.GamePlay.Managers;
using Unity.VisualScripting;
using UnityEngine;

namespace JellyGame.GamePlay.UI
{
    public class Explosion : MonoBehaviour
    {
        [SerializeField] private GameObject explosionEffect;
        [SerializeField] private float effectDuration = 20f;
        [SerializeField] private GameObject[] chainsToExplode;
        [SerializeField] private GameObject chainFall;

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
            {
                explosionEffect = GameObject.FindGameObjectWithTag("Explosion");
            }

            if (chainsToExplode == null || chainsToExplode.Length == 0)
            {
                chainsToExplode = GameObject.FindGameObjectsWithTag("Chains");
            }

            if (chainFall == null)
            {
                chainFall = GameObject.FindGameObjectWithTag("ChainFall");
            }
        }

        private void OnExplosion(object eventdata)
        {
            if (explosionEffect != null)
            {
                explosionEffect.SetActive(true);
                for (int i = 0; i < chainsToExplode.Length; i++)
                {
                    if (chainsToExplode[i] != null)
                    {
                        chainsToExplode[i].SetActive(false);
                    }
                }

                StartCoroutine(DeactivateExplosionEffect(effectDuration));
                if (chainFall != null)
                {
                    chainFall.SetActive(true);
                }

                StartCoroutine(DeactivateChainFall(5f));
                EventManager.TriggerEvent(EventManager.GameEvent.PortalLvl3, null);
            }
        }

        private IEnumerator DeactivateExplosionEffect(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (explosionEffect != null)
            {
                explosionEffect.SetActive(false);
            }
        }

        private IEnumerator DeactivateChainFall(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (chainFall != null)
            {
                chainFall.SetActive(false);
            }
        }
    }
}