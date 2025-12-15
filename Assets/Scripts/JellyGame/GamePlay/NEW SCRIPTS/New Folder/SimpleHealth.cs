// FILEPATH: Assets/Scripts/Combat/SimpleHealth.cs
using UnityEngine;

namespace JellyGame.GamePlay.Combat
{
    [DisallowMultipleComponent]
    public class SimpleHealth : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHp = 10f;
        [SerializeField] private bool destroyOnDeath = true;

        private float _hp;

        private void Awake()
        {
            _hp = maxHp;
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;

            _hp -= amount;
            Debug.Log(amount + " HP: " + _hp);
            if (_hp <= 0f)
            {
                _hp = 0f;
                if (destroyOnDeath)
                    Destroy(gameObject);
            }
        }
    }
}