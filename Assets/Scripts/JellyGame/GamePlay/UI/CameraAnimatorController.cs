using System;
using UnityEngine;
using JellyGame.GamePlay.Managers;

public class CameraAnimatorController : MonoBehaviour
{
    [SerializeField] private Animator camAnimator;
    [SerializeField] private string shakeBoolName = "shaking";
    [SerializeField] private string startLvl3Gameplay = "start lvl 3 gameplay";

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

    private void Start()
    {
        if(camAnimator) camAnimator.SetBool(startLvl3Gameplay, true);
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