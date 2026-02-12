using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers;

public class PrimeHitReaction : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Drag the Slime Prime Animator here.")]
    [SerializeField] private Animator[] targetAnimators;

    [Header("Parameter Names")]
    [SerializeField] private string castOutParam = "cast out";
    [SerializeField] private string castInParam = "cast in";
    [SerializeField] private string castingParam = "casting"; 

    [Header("Durations")]
    [Tooltip("How long to stay in Cast Out state")]
    [SerializeField] private float castOutDuration = 2.0f;
    
    [Tooltip("How long to stay in Idle before casting in again")]
    [SerializeField] private float idleDuration = 1.0f;

    private Coroutine _sequenceCoroutine;

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
            return;

        if (_sequenceCoroutine != null) 
            StopCoroutine(_sequenceCoroutine);

        _sequenceCoroutine = StartCoroutine(HitSequence());
    }

    private IEnumerator HitSequence()
    {
        foreach (var anim in targetAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castingParam, false); 
            anim.SetBool(castInParam, false); 
            anim.SetBool(castOutParam, true); 
        }

        yield return new WaitForSeconds(castOutDuration);
        
        foreach (var anim in targetAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castOutParam, false);
        }

        yield return new WaitForSeconds(idleDuration);
        
        foreach (var anim in targetAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castInParam, true);
        }
      
        yield return new WaitForSeconds(1.0f); 

        foreach (var anim in targetAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castInParam, false);
            anim.SetBool(castingParam, true); 
        }
    }
}