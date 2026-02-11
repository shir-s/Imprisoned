using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers;

public class PrimeHitReaction : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Drag all Animators you want to trigger here (e.g., Timer Base and Timer Effect).")]
    [SerializeField] private Animator[] targetAnimators; // Changed to Array

    [Tooltip("The exact parameter name (case sensitive).")]
    [SerializeField] private string boolParamName = "prime is attcked"; 

    [Tooltip("How long the animation state should stay active (in seconds).")]
    [SerializeField] private float effectDuration = 0.5f;

    private Coroutine _resetCoroutine;

    private void OnEnable()
    {
        EventManager.StartListening(EventManager.GameEvent.SlimePrimeDamaged, OnPrimeHit);
    }

    private void OnDisable()
    {
        EventManager.StopListening(EventManager.GameEvent.SlimePrimeDamaged, OnPrimeHit);
    }

    private void OnPrimeHit(object data)
    {
        if (targetAnimators == null || targetAnimators.Length == 0) 
        {
            return;
        }

        if (_resetCoroutine != null) 
            StopCoroutine(_resetCoroutine);

        _resetCoroutine = StartCoroutine(HitSequence());
    }

    private IEnumerator HitSequence()
    {
        // 1. Activate bool for ALL animators
        foreach (var anim in targetAnimators)
        {
            if (anim != null)
                anim.SetBool(boolParamName, true);
        }

        yield return new WaitForSeconds(effectDuration);

        // 2. Deactivate bool for ALL animators
        foreach (var anim in targetAnimators)
        {
            if (anim != null)
                anim.SetBool(boolParamName, false);
        }
    }
}