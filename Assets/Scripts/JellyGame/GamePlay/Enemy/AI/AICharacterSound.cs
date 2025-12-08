// FILEPATH: Assets/Scripts/AI/AICharacterSound.cs

using JellyGame.GamePlay.Audio.Core;
using UnityEngine;
using AudioSettings = JellyGame.GamePlay.Audio.Core.AudioSettings;

namespace JellyGame.GamePlay.Enemy.AI
{
    /// <summary>
    /// Manages sound playback for AI characters based on their active behavior.
    /// 
    /// Works with BehaviorManager to detect behavior changes and play sounds accordingly.
    /// Behaviors can optionally implement IEnemySound to control their sound playback.
    /// 
    /// Setup:
    /// 1. Attach this component to the same GameObject as BehaviorManager
    /// 2. Sounds will automatically play based on active behavior
    /// 3. Behaviors that implement IEnemySound will have custom sound control
    /// 
    /// Example:
    /// - WanderBehavior plays "EnemyWander" every 3 seconds
    /// - AttackBehavior plays "EnemyAttack" on enter, then loops "EnemyChase"
    /// - FollowStrokeBehavior plays "EnemyAlert" at random intervals
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BehaviorManager))]
    public class AICharacterSound : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("BehaviorManager on this GameObject. Auto-assigned if left empty.")]
        [SerializeField] private BehaviorManager behaviorManager;

        [Header("Default Sound Settings")]
        [Tooltip("Default sound name to play if behavior doesn't specify one.")]
        [SerializeField] private string defaultSoundName = "";

        [Tooltip("Default interval for FixedInterval mode (seconds).")]
        [SerializeField] private float defaultInterval = 2f;

        [Tooltip("Default mode if behavior doesn't implement IEnemySound.")]
        [SerializeField] private SoundPlaybackMode defaultMode = SoundPlaybackMode.FixedInterval;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        // Current state
        private IEnemyBehavior _currentBehavior;
        private IEnemySound _currentSoundBehavior;
        private SoundPlaybackMode _currentMode = SoundPlaybackMode.None;
        private float _soundTimer;
        private float _currentInterval;
        private AudioSourceWrapper _loopingSound;

        private void Awake()
        {
            if (behaviorManager == null)
            {
                behaviorManager = GetComponent<BehaviorManager>();
            }

            if (behaviorManager == null)
            {
                Debug.LogError("[AICharacterSound] No BehaviorManager found on this GameObject!", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (behaviorManager == null)
                return;

            // Check if behavior changed
            IEnemyBehavior currentBehavior = GetCurrentBehavior();
        
            if (currentBehavior != _currentBehavior)
            {
                OnBehaviorChanged(_currentBehavior, currentBehavior);
                _currentBehavior = currentBehavior;
            }

            // Update sound playback based on current mode
            if (_currentBehavior != null && _currentMode != SoundPlaybackMode.None)
            {
                UpdateSoundPlayback(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            StopLoopingSound();
        }

        private void OnDisable()
        {
            StopLoopingSound();
        }

        /// <summary>
        /// Get the current active behavior from BehaviorManager.
        /// Uses reflection to access the private _currentBehavior field.
        /// </summary>
        private IEnemyBehavior GetCurrentBehavior()
        {
            if (behaviorManager == null)
                return null;

            // Access private _currentBehavior field via reflection
            var field = typeof(BehaviorManager).GetField("_currentBehavior", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
            if (field != null)
            {
                return field.GetValue(behaviorManager) as IEnemyBehavior;
            }

            return null;
        }

        private void OnBehaviorChanged(IEnemyBehavior oldBehavior, IEnemyBehavior newBehavior)
        {
            if (debugLogs)
            {
                string oldName = oldBehavior != null ? oldBehavior.GetType().Name : "null";
                string newName = newBehavior != null ? newBehavior.GetType().Name : "null";
                Debug.Log($"[AICharacterSound] Behavior changed: {oldName} → {newName}", this);
            }

            // Handle exit sound for old behavior
            if (oldBehavior != null && oldBehavior is IEnemySound oldSound)
            {
                if (oldSound.GetSoundMode() == SoundPlaybackMode.OnExit)
                {
                    PlaySound(oldSound);
                }
            }

            // Stop any looping sound
            StopLoopingSound();

            // Setup new behavior sound
            _currentSoundBehavior = newBehavior as IEnemySound;
            _soundTimer = 0f;

            if (newBehavior == null)
            {
                _currentMode = SoundPlaybackMode.None;
                return;
            }

            // Determine sound mode
            if (_currentSoundBehavior != null)
            {
                _currentMode = _currentSoundBehavior.GetSoundMode();
                _currentInterval = _currentSoundBehavior.GetSoundInterval();
            }
            else
            {
                // Use default settings if behavior doesn't implement IEnemySound
                _currentMode = defaultMode;
                _currentInterval = defaultInterval;
            }

            // Handle immediate sound modes
            switch (_currentMode)
            {
                case SoundPlaybackMode.OnEnter:
                case SoundPlaybackMode.OnEnterLoop:
                    PlaySound(_currentSoundBehavior);
                
                    if (_currentMode == SoundPlaybackMode.OnEnterLoop)
                    {
                        StartLoopingSound();
                    }
                    break;

                case SoundPlaybackMode.Loop:
                    StartLoopingSound();
                    break;

                case SoundPlaybackMode.RandomInterval:
                    // Randomize first interval
                    RandomizeInterval();
                    break;
            }
        }

        private void UpdateSoundPlayback(float deltaTime)
        {
            switch (_currentMode)
            {
                case SoundPlaybackMode.FixedInterval:
                case SoundPlaybackMode.RandomInterval:
                    _soundTimer -= deltaTime;

                    if (_soundTimer <= 0f)
                    {
                        PlaySound(_currentSoundBehavior);

                        if (_currentMode == SoundPlaybackMode.RandomInterval)
                        {
                            RandomizeInterval();
                        }
                        else
                        {
                            _soundTimer = _currentInterval;
                        }
                    }
                    break;
            }
        }

        private void RandomizeInterval()
        {
            float minInterval = _currentInterval;
            float maxInterval = _currentSoundBehavior != null 
                ? _currentSoundBehavior.GetMaxSoundInterval() 
                : _currentInterval * 2f;

            _soundTimer = Random.Range(minInterval, maxInterval);

            if (debugLogs)
            {
                Debug.Log($"[AICharacterSound] Next sound in {_soundTimer:F2}s", this);
            }
        }

        private void PlaySound(IEnemySound soundBehavior)
        {
            // Check if sound should be played
            if (soundBehavior != null && !soundBehavior.ShouldPlaySound())
            {
                if (debugLogs)
                {
                    Debug.Log("[AICharacterSound] Sound skipped (ShouldPlaySound returned false)", this);
                }
                return;
            }

            // Get sound name
            string soundName = soundBehavior != null 
                ? soundBehavior.GetSoundName() 
                : defaultSoundName;

            if (string.IsNullOrEmpty(soundName))
            {
                if (debugLogs)
                {
                    Debug.Log("[AICharacterSound] No sound name specified", this);
                }
                return;
            }

            // Get custom volume
            float volume = soundBehavior != null ? soundBehavior.GetSoundVolume() : -1f;

            // Play sound via SoundManager
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySound(soundName, transform, volume);

                if (debugLogs)
                {
                    Debug.Log($"[AICharacterSound] Playing sound: {soundName} (volume: {(volume >= 0 ? volume.ToString("F2") : "default")})", this);
                }
            }
            else
            {
                Debug.LogWarning("[AICharacterSound] SoundManager.Instance is null!", this);
            }
        }

        private void StartLoopingSound()
        {
            StopLoopingSound(); // Stop any existing loop

            string soundName = _currentSoundBehavior != null 
                ? _currentSoundBehavior.GetSoundName() 
                : defaultSoundName;

            if (string.IsNullOrEmpty(soundName))
                return;

            if (SoundManager.Instance != null)
            {
                // For looping sounds, we play with loop enabled
                // Note: Your SoundManager already supports loop via AudioConfig.loop
                // But we're playing it programmatically, so we need to handle it specially
            
                float volume = _currentSoundBehavior != null ? _currentSoundBehavior.GetSoundVolume() : -1f;
            
                // We'll use the pool directly for looping
                _loopingSound = SoundPool.Instance.Get();
                _loopingSound.transform.position = transform.position;
            
                // Find the audio config
                var config = FindAudioConfig(soundName);
                if (config != null)
                {
                    float finalVolume = (volume >= 0f) ? volume : config.volume;
                    _loopingSound.Play(config.clip, finalVolume, true); // Force loop = true

                    if (debugLogs)
                    {
                        Debug.Log($"[AICharacterSound] Started looping sound: {soundName}", this);
                    }
                }
                else
                {
                    SoundPool.Instance.Return(_loopingSound);
                    _loopingSound = null;
                }
            }
        }

        private void StopLoopingSound()
        {
            if (_loopingSound != null)
            {
                if (debugLogs)
                {
                    Debug.Log("[AICharacterSound] Stopping looping sound", this);
                }

                _loopingSound.Reset();
                SoundPool.Instance.Return(_loopingSound);
                _loopingSound = null;
            }
        }

        /// <summary>
        /// Helper to find audio config from SoundManager's settings.
        /// </summary>
        private AudioConfig FindAudioConfig(string soundName)
        {
            if (SoundManager.Instance == null)
                return null;

            // Access the settings via reflection
            var settingsField = typeof(SoundManager).GetField("settings", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
            if (settingsField != null)
            {
                // Fully-qualify to avoid ambiguity with UnityEngine.AudioSettings
                var settings = settingsField.GetValue(SoundManager.Instance) as AudioSettings;
                if (settings != null && settings.audioConfigs != null)
                {
                    foreach (var config in settings.audioConfigs)
                    {
                        if (config.name == soundName)
                            return config;
                    }
                }
            }

            Debug.LogWarning($"[AICharacterSound] Audio config not found: {soundName}", this);
            return null;
        }

        /// <summary>
        /// Manually trigger a sound play (useful for events).
        /// </summary>
        public void PlaySoundManually(string soundName, float volume = -1f)
        {
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySound(soundName, transform, volume);
            }
        }
    }
}
