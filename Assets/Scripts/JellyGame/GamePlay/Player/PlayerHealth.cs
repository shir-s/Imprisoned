using UnityEngine;
using UnityEngine.Events;
namespace JellyGame.GamePlay.Player
{
    [DisallowMultipleComponent]
    public class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 3;
        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }

        public UnityEvent onPlayerDeath;

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
            onPlayerDeath?.Invoke();
            
            Destroy(gameObject);
        }
    }
}