// FILEPATH: Assets/Scripts/Combat/SimpleHealth.cs
using JellyGame.GamePlay.Managers;
using UnityEngine;

namespace JellyGame.GamePlay.Combat
{
    [DisallowMultipleComponent]
    public class SimpleHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHp = 10f;
        [SerializeField] private bool destroyOnDeath = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private float _hp;
        private bool _dead;

        private void Awake()
        {
            _hp = maxHp;
            _dead = false;
        }

        public void ApplyDamage(float amount)
        {
            if (_dead)
                return;

            if (amount <= 0f)
                return;

            _hp -= amount;

            if (debugLogs)
                Debug.Log($"{amount:F1} HP: {_hp}", this);

            if (_hp > 0f)
                return;

            _hp = 0f;
            Die();
        }

        private void Die()
        {
            if (_dead)
                return;

            _dead = true;

            // Trigger universal event BEFORE destruction.
            EventManager.TriggerEvent(
                EventManager.GameEvent.EntityDied,
                new EntityDiedEventData(gameObject, gameObject.layer)
            );

            if (destroyOnDeath)
                Destroy(gameObject);
        }
    }
}