// FILEPATH: Assets/Scripts/UI/Tutorial/OrderedTriggerStep.cs

using UnityEngine;

namespace JellyGame.UI.Tutorial
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class OrderedTriggerStep : MonoBehaviour
    {
        [SerializeField] private OrderedTriggerSequence sequence;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (sequence != null)
                sequence.HandleTriggerEnter(GetComponent<OrderedTriggerForwarder>(), other);
        }
    }
}