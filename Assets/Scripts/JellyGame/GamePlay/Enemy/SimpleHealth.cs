// FILEPATH: Assets/Scripts/Combat/SimpleHealth.cs

using JellyGame.GamePlay.Audio.Core;
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.Combat
{
    [DisallowMultipleComponent]
    public class SimpleHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHp = 10f;
        [SerializeField] private bool destroyOnDeath = true;
        [SerializeField] private GameObject particleSystem;
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private float _hp;
        private bool _dead;

        private void Awake()
        {
            _hp = maxHp;
            _dead = false;
            if(particleSystem != null)
            {
                particleSystem.SetActive(false);
            }
        }

        public void ApplyDamage(float amount)
        {
            if (_dead)
                return;

            if (amount <= 0f)
                return;

            _hp -= amount;

            if (debugLogs)
                Debug.Log($"{this.name} {amount:F1} HP: {_hp}", this);

            if (_hp > 0f)
                return;

            _hp = 0f;
            Die();
        }

        public void Heal(float amount)
        {
            _hp += amount;
        }

        private void Die()
        {
            if (_dead)
                return;

            _dead = true;
            
            SoundManager.Instance.PlaySound("EnemyDeath", transform);

            // Trigger universal event BEFORE destruction.
            EventManager.TriggerEvent(
                EventManager.GameEvent.EntityDied,
                new EntityDiedEventData(gameObject, gameObject.layer)
            );
            if (particleSystem != null)
            {
                particleSystem.SetActive(true);
                Instantiate(particleSystem, transform.position, Quaternion.identity);
            }

            if (destroyOnDeath)
                Destroy(gameObject);
        }
    }
}