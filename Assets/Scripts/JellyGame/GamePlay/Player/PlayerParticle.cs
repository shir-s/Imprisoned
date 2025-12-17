using JellyGame.GamePlay.Managers;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.VFX;

namespace JellyGame.GamePlay.Player
{
    public class PlayerParticle : MonoBehaviour
    {
        [SerializeField] private VisualEffect hitParticleEffect;
        
        void OnEnable()
        {
            EventManager.StartListening(EventManager.GameEvent.PlayerDamaged, OnPlayerDamaged);
        }
        
        void OnDisable()
        {
            EventManager.StopListening(EventManager.GameEvent.PlayerDamaged, OnPlayerDamaged);
        }
        
        void Awake()
        {
            if (hitParticleEffect == null)
            {
                Debug.LogWarning("PlayerParticle.Awake() - No hit particle effect assigned!", this);
            }
        }

        private void OnPlayerDamaged(object eventdata)
        {
            if (hitParticleEffect != null)
            {
                Debug.Log("PlayerParticle.OnPlayerDamaged() - Playing hit particle effect");
                hitParticleEffect.SendEvent("OnPlay");
                //hitParticleEffect.Play();
            }
        }
    }
}