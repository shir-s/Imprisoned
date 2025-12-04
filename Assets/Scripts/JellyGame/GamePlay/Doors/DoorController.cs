using UnityEngine;

namespace JellyGame.GamePlay.Doors
{
    [DisallowMultipleComponent]
    public class DoorController : MonoBehaviour
    {
        [Header("Door Parts")]
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;

        [Header("Settings")]
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float openDuration = 1f;

        bool _isOpen;
        Quaternion _leftClosedRot, _rightClosedRot;
        Quaternion _leftOpenRot, _rightOpenRot;

        void Awake()
        {
            _leftClosedRot = leftDoor.localRotation;
            _rightClosedRot = rightDoor.localRotation;

            _leftOpenRot = _leftClosedRot * Quaternion.Euler(0, -openAngle, 0);
            _rightOpenRot = _rightClosedRot * Quaternion.Euler(0, openAngle, 0);
        }

        public void OpenDoor()
        {
            if (_isOpen) return;
            StopAllCoroutines();
            StartCoroutine(AnimateDoor(_leftOpenRot, _rightOpenRot, true));
        }

        System.Collections.IEnumerator AnimateDoor(Quaternion leftTarget, Quaternion rightTarget, bool opening)
        {
            float t = 0f;
            Quaternion leftStart = leftDoor.localRotation;
            Quaternion rightStart = rightDoor.localRotation;

            while (t < openDuration)
            {
                float lerp = t / openDuration;
                leftDoor.localRotation = Quaternion.Lerp(leftStart, leftTarget, lerp);
                rightDoor.localRotation = Quaternion.Lerp(rightStart, rightTarget, lerp);
                t += Time.deltaTime;
                yield return null;
            }

            leftDoor.localRotation = leftTarget;
            rightDoor.localRotation = rightTarget;
            _isOpen = opening;
        }
    }
}