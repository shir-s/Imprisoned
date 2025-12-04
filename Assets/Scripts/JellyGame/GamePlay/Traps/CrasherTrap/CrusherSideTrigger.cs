using UnityEngine;
namespace JellyGame.GamePlay.Traps
{
    /// <summary>
    /// Trigger placed on each side of the crusher (left/right).
    /// Reports enter/exit to the CrusherTrap.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrusherSideTrigger : MonoBehaviour
    {
        public enum Side { Left, Right }

        [SerializeField] private Side side;
        [SerializeField] private CrusherTrap crusher;

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (crusher == null) return;
            crusher.RegisterSideContact(side, other);
        }

        void OnTriggerExit(Collider other)
        {
            if (crusher == null) return;
            crusher.UnregisterSideContact(side, other);
        }
    }

}