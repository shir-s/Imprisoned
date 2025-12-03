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

        void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(int amount)
        {
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
            onEnemyDeath?.Invoke();
            Destroy(gameObject);
        }
    }
}