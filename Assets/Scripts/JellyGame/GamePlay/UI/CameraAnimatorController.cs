using UnityEngine;
using JellyGame.GamePlay.Managers;

public class CameraAnimatorController : MonoBehaviour
{
    [SerializeField] private Animator camAnimator;
    [SerializeField] private string shakeBoolName = "shaking";

    private void OnEnable()
    {
        EventManager.StartListening(EventManager.GameEvent.CountdownTimerLowTime, StartAnim);
        EventManager.StartListening(EventManager.GameEvent.Explosion, StopAnim);
    }

    private void OnDisable()
    {
        EventManager.StopListening(EventManager.GameEvent.CountdownTimerLowTime, StartAnim);
        EventManager.StopListening(EventManager.GameEvent.Explosion, StopAnim);
    }

    private void StartAnim(object data)
    {
        if (camAnimator) camAnimator.SetBool(shakeBoolName, true);
    }

    private void StopAnim(object data)
    {
        if (camAnimator) camAnimator.SetBool(shakeBoolName, false);
    }
}