using UnityEngine;
using UnityEngine.Events;

namespace JellyGame.GamePlay.Doors
{
    public class DoorController : MonoBehaviour
    {
        [Header("Door Parts")]
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;

        [Header("Settings")]
        [SerializeField] private float openAngle = 90f;      // כמה הדלתות ייפתחו
        [SerializeField] private float openDuration = 1f;    // כמה זמן האנימציה
        [SerializeField] private bool startClosed = true;

        [Header("Events")]
        public UnityEvent OnDoorOpened;   // תוכלי לחבר כל דבר מהאינספקטור
        public UnityEvent OnDoorClosed;

        bool isOpen = false;
        Quaternion leftClosedRot, rightClosedRot;
        Quaternion leftOpenRot, rightOpenRot;

        void Awake()
        {
            // שמירת הרוטציות המקוריות
            leftClosedRot = leftDoor.localRotation;
            rightClosedRot = rightDoor.localRotation;

            // חישוב הרוטציה הפתוחה
            leftOpenRot = leftClosedRot * Quaternion.Euler(0, -openAngle, 0);
            rightOpenRot = rightClosedRot * Quaternion.Euler(0, openAngle, 0);

            if (!startClosed)
            {
                leftDoor.localRotation = leftOpenRot;
                rightDoor.localRotation = rightOpenRot;
                isOpen = true;
            }
        }

        public void ToggleDoor()
        {
            if (isOpen)
                CloseDoor();
            else
                OpenDoor();
        }

        public void OpenDoor()
        {
            if (!isOpen)
                StartCoroutine(AnimateDoor(leftOpenRot, rightOpenRot, true));
        }

        public void CloseDoor()
        {
            if (isOpen)
                StartCoroutine(AnimateDoor(leftClosedRot, rightClosedRot, false));
        }

        private System.Collections.IEnumerator AnimateDoor(Quaternion leftTarget, Quaternion rightTarget, bool opening)
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

            isOpen = opening;

            if (opening)
                OnDoorOpened?.Invoke();
            else
                OnDoorClosed?.Invoke();
        }
    }
}
