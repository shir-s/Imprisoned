// FILEPATH: Assets/Scripts/AI/Behaviors/AttackFXHandler.cs
using UnityEngine;

namespace JellyGame.GamePlay.Enemy.AI.Behaviors
{
    /// <summary>
    /// Place this on the same GameObject that has the Animator.
    /// Animation events in the attack clip call ActivateAttackFX() and DeactivateAttackFX().
    ///
    /// Setup:
    /// 1. Add this component to the object with the Animator (may be a child of the enemy root).
    /// 2. Drag the particle system GameObject into "Attack FX".
    /// 3. In the attack animation clip, add two Animation Events:
    ///    - At the desired mid-point frame → call "ActivateAttackFX"
    ///    - At the end (or near end) frame  → call "DeactivateAttackFX"
    /// </summary>
    [DisallowMultipleComponent]
    public class AttackFXHandler : MonoBehaviour
    {
        [Tooltip("The particle system GameObject to toggle during the attack animation.")]
        [SerializeField] private GameObject attackFX;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private void Start()
        {
            // Make sure it starts off
            if (attackFX != null)
                attackFX.SetActive(false);
        }

        /// <summary>Called by Animation Event at the mid-point of the attack clip.</summary>
        public void ActivateAttackFX()
        {
            if (attackFX == null) return;

            attackFX.SetActive(true);

            // Also restart particle systems in case they already played
            var systems = attackFX.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }

            if (debugLogs)
                Debug.Log("[AttackFXHandler] Attack FX activated.", this);
        }

        /// <summary>Called by Animation Event at the end of the attack clip.</summary>
        public void DeactivateAttackFX()
        {
            if (attackFX == null) return;

            attackFX.SetActive(false);

            if (debugLogs)
                Debug.Log("[AttackFXHandler] Attack FX deactivated.", this);
        }
    }
}