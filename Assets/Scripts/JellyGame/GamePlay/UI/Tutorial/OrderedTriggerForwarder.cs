// FILEPATH: Assets/Scripts/UI/Tutorial/OrderedTriggerForwarder.cs
using UnityEngine;

namespace JellyGame.UI.Tutorial
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class OrderedTriggerForwarder : MonoBehaviour
    {
        [SerializeField] private OrderedTriggerSequence sequence;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            if (sequence == null)
                sequence = GetComponentInParent<OrderedTriggerSequence>();
        }

        private void OnTriggerEnter(Collider other)
        {
            sequence?.HandleTriggerEnter(this, other);
        }

        private void OnTriggerExit(Collider other)
        {
            sequence?.HandleTriggerExit(this, other);
        }
    }
}