using JellyGame.GamePlay.Painting;
using JellyGame.GamePlay.Painting.Trails;
using JellyGame.GamePlay.Painting.Trails.Visibility;
using UnityEngine;

namespace JellyGame.GamePlay.Audio.Painting
{
    [RequireComponent(typeof(MovementPaintController))]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(Rigidbody))]
    public class SoundPainterController : MonoBehaviour
    {
        [SerializeField] private MovementPaintController paintController;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Rigidbody rb;

        [Header("Speed → Volume")]
        [SerializeField] private float minSpeed = 0.05f;
        [SerializeField] private float maxSpeed = 3f;
        [SerializeField] private float fadeSpeed = 8f; 

        private float targetVolume = 0f;
        private float enableTime;

        private void Reset()
        {
            paintController = GetComponent<MovementPaintController>();
            audioSource     = GetComponent<AudioSource>();
            rb              = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (paintController == null)
                paintController = GetComponent<MovementPaintController>();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (rb == null)
                rb = GetComponent<Rigidbody>();

            audioSource.loop        = true;
            audioSource.playOnAwake = false;
            audioSource.volume      = 0f;
        }

        private void OnEnable()
        {
            enableTime = Time.time;

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
                audioSource.Play();
            else if (audioSource.volume <= 0.001f && audioSource.isPlaying)
                audioSource.Stop();
        }

        private void HandlePaintingUpdate(float _, bool isPainting)
        {
            if (Time.time - enableTime < 0.05f)
            {
                targetVolume = 0f;
                return;
            }

            if (!isPainting || rb == null)
            {
                targetVolume = 0f;
                return;
            }

            Vector3 v = rb.linearVelocity; 
            float speed = v.magnitude;  

            if (speed < minSpeed)
            {
                targetVolume = 0f;
                return;
            }

            float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
            t = Mathf.Clamp01(t);

            t = t * t;

            targetVolume = t;
        }
    }
}
