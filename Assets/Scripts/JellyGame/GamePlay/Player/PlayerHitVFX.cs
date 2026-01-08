using UnityEngine;
using System.Collections;
using JellyGame.GamePlay.Managers;

public class PlayerHitVFX : MonoBehaviour
{
    [Header("Settings")]
    public Material slimeHitMaterial; 
    public string intensityParameter = "_VignetteIntencity"; 
    public float maxIntensity = 63f;
    public float duration = 1.2f;

    private Coroutine _hitCoroutine;
    
    void OnEnable()
    {
        EventManager.StartListening(EventManager.GameEvent.PlayerDamaged, OnPlayerDamaged);
    }

    private void OnPlayerDamaged(object eventdata)
    {
        PlayHitEffect();
    }

    public void PlayHitEffect()
    {
        if (_hitCoroutine != null)
            StopCoroutine(_hitCoroutine);

        _hitCoroutine = StartCoroutine(HitEffectRoutine());
    }

    private IEnumerator HitEffectRoutine()
    {
        float halfDuration = duration / 2f;
        float elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float currentVal = Mathf.Lerp(0, maxIntensity, elapsed / halfDuration);
            slimeHitMaterial.SetFloat(intensityParameter, currentVal);
            yield return null;
        }

        elapsed = 0f;

        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float currentVal = Mathf.Lerp(maxIntensity, 0, elapsed / halfDuration);
            slimeHitMaterial.SetFloat(intensityParameter, currentVal);
            yield return null;
        }

        slimeHitMaterial.SetFloat(intensityParameter, 0);
    }

    private void OnDisable()
    {
        if (slimeHitMaterial != null)
            slimeHitMaterial.SetFloat(intensityParameter, 0);
        EventManager.StopListening(EventManager.GameEvent.PlayerDamaged, OnPlayerDamaged);
    }
}