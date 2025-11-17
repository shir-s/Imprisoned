using UnityEngine;

[RequireComponent(typeof(MovementPaintController))]
[RequireComponent(typeof(AudioSource))]
public class SoundPainterController : MonoBehaviour
{
    [SerializeField] private MovementPaintController paintController;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Rigidbody rb;   


    [Header("Speed → Volume")]
    [SerializeField] private float minSpeed = 0.05f;  
    [SerializeField] private float maxSpeed = 0.4f; 
    [SerializeField] private float fadeSpeed = 8f; 

    private float targetVolume = 0f;

    private void Reset()
    {
        paintController = GetComponent<MovementPaintController>();
        audioSource     = GetComponent<AudioSource>();
    }

    private void Awake()
    {
        if (paintController == null)
            paintController = GetComponent<MovementPaintController>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = 0f;
    }

    private void OnEnable()
    {
        if (paintController != null)
            paintController.OnPaintingUpdate += HandlePaintingUpdate;
    }

    private void OnDisable()
    {
        if (paintController != null)
            paintController.OnPaintingUpdate -= HandlePaintingUpdate;
    }

    private void Update()
    {
        if (audioSource == null)
            return;

        audioSource.volume = Mathf.MoveTowards(
            audioSource.volume,
            targetVolume,
            fadeSpeed * Time.deltaTime
        );

        if (audioSource.volume > 0.001f && !audioSource.isPlaying)
        {
            audioSource.Play();
        }
        else if (audioSource.volume <= 0.001f && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    // private void HandlePaintingUpdate(float speed, bool isPainting)
    // {
    //     if (!isPainting)
    //     {
    //         targetVolume = 0f;
    //         return;
    //     }
    //
    //     float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
    //     targetVolume = Mathf.Clamp01(t);
    // }
    
    private void HandlePaintingUpdate(float _, bool isPainting)
    {
        if (!isPainting)
        {
            targetVolume = 0f;
            return;
        }

        if (rb == null)
        {
            targetVolume = 0f;
            return;
        }

        float speed = rb.linearVelocity.magnitude;

        // Debug.Log("RB speed = " + speed);

        float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);

        t = Mathf.Clamp01(t);
        t = t * t;  

        targetVolume = t;
    }

}
