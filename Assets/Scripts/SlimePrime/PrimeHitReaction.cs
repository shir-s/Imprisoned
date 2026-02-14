
using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers;

public class PrimeHitReaction : MonoBehaviour
{
    // ========================================================================
    // חלק 1: האפקט הפשוט (הבהוב/רעידה לטיימרים)
    // ========================================================================
    [Header("--- Part 1: Simple Hit Effect ---")]
    [Tooltip("Drag Animators that just need a quick hit reaction (e.g. Timer Effects).")]
    [SerializeField] private Animator[] effectAnimators; 

    [Tooltip("The parameter for the simple hit effect.")]
    [SerializeField] private string hitBoolParam = "prime is attcked";

    [Tooltip("Duration for the simple hit effect.")]
    [SerializeField] private float hitDuration = 0.5f;

    // ========================================================================
    // חלק 2: הרצף המורכב (הסליים נעלם וחוזר)
    // ========================================================================
    [Header("--- Part 2: Slime Sequence ---")]
    [Tooltip("Drag the Slime Prime Animator here for the Cast Out/In sequence.")]
    [SerializeField] private Animator[] slimeAnimators;

    [Header("Slime Parameters")]
    [SerializeField] private string castOutParam = "cast out";
    [SerializeField] private string castInParam = "cast in";
    [SerializeField] private string castingParam = "casting"; 

    [Header("Slime Durations")]
    [SerializeField] private float castOutDuration = 2.0f;
    [SerializeField] private float idleDuration = 1.0f;

    // שני משתנים נפרדים כדי שקורוטינה אחת לא תעצור את השנייה
    private Coroutine _effectCoroutine;
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
        // 1. הפעלת האפקט הפשוט (הישן) - רץ במקביל
        if (effectAnimators != null && effectAnimators.Length > 0)
        {
            if (_effectCoroutine != null) StopCoroutine(_effectCoroutine);
            _effectCoroutine = StartCoroutine(SimpleHitRoutine());
        }

        // 2. הפעלת הרצף המורכב של הסליים (החדש) - רץ במקביל
        if (slimeAnimators != null && slimeAnimators.Length > 0)
        {
            if (_sequenceCoroutine != null) StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = StartCoroutine(SlimeSequenceRoutine());
        }
    }

    // ------------------------------------------------------------------------
    // לוגיקה 1: האפקט הקצר (0.5 שניות)
    // ------------------------------------------------------------------------
    private IEnumerator SimpleHitRoutine()
    {
        // הדלקה
        foreach (var anim in effectAnimators)
        {
            if (anim != null) anim.SetBool(hitBoolParam, true);
        }

        yield return new WaitForSeconds(hitDuration);

        // כיבוי
        foreach (var anim in effectAnimators)
        {
            if (anim != null) anim.SetBool(hitBoolParam, false);
        }
    }

    // ------------------------------------------------------------------------
    // לוגיקה 2: הרצף המורכב (Cast Out -> Idle -> Cast In)
    // ------------------------------------------------------------------------
    private IEnumerator SlimeSequenceRoutine()
    {
        // שלב 1: Cast Out (פגיעה)
        foreach (var anim in slimeAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castingParam, false); 
            anim.SetBool(castInParam, false); 
            anim.SetBool(castOutParam, true); 
        }

        yield return new WaitForSeconds(castOutDuration);
        
        // שלב 2: Idle
        foreach (var anim in slimeAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castOutParam, false);
        }

        yield return new WaitForSeconds(idleDuration);
        
        // שלב 3: Cast In (חזרה)
        foreach (var anim in slimeAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castInParam, true);
        }
      
        yield return new WaitForSeconds(1.0f); 

        // שלב 4: חזרה ללופ הקסמים (Casting Loop)
        foreach (var anim in slimeAnimators)
        {
            if (anim == null) continue;
            anim.SetBool(castInParam, false);
            anim.SetBool(castingParam, true); 
        }
    }
}
