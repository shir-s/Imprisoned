using System;
using System.Collections;
using JellyGame.GamePlay.Utils;
using JellyGame.GamePlay.Managers;
using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.GamePlay.Enemy
{

    [DisallowMultipleComponent]
    public class EnemyHealth : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 1;
        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }

        public UnityEvent onEnemyDeath;

        // event קוד-צדדי – לאוניטי אין מושג ממנו, זה רק למנהלים
        public event Action<EnemyHealth> EnemyDied;

        void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(int amount)
        {
            print("EnemyHealth.TakeDamage(" + amount + ")");
            if (CurrentHealth <= 0) return;

            CurrentHealth -= amount;
            if (CurrentHealth <= 0)
            {
                CurrentHealth = 0;
                Die();
            }
        }

        public void Kill()
        {
            TakeDamage(CurrentHealth);
        }

        void Die()
        {
            print("EnemyHealth.Die()");
            
            // Trigger events immediately (before delay)
            onEnemyDeath?.Invoke();       
            EnemyDied?.Invoke(this);     

            // Capture position BEFORE destroying (critical!)
            Vector3 deathPosition = transform.position;
            
            // Trigger EventManager event (like SimpleHealth does, for DoorByDeaths compatibility)
            EventManager.TriggerEvent(
                EventManager.GameEvent.EntityDied,
                new EntityDiedEventData(gameObject, gameObject.layer)
            );
            
            // Trigger JellyGameEvents
            JellyGameEvents.EnemyDied?.Invoke(deathPosition);
            
            // Start coroutine to destroy after 2 seconds
            StartCoroutine(DestroyAfterDelay());
        }

        private IEnumerator DestroyAfterDelay()
        {
            // Wait 2 seconds before destroying
            yield return new WaitForSeconds(1f);
            
            // Destroy the enemy after the delay
            Destroy(gameObject);
        }
    }
}