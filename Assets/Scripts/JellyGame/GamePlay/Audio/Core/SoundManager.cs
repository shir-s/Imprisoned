using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JellyGame.GamePlay.Managers;
using UnityEngine;
using Utils;

namespace JellyGame.GamePlay.Audio.Core
{
    public class SoundManager : MonoSingleton<SoundManager>
    {
        [SerializeField] private AudioSettings settings;
        //private AudioSourceWrapper _backgroundMusic;
        
        // private void Start()
        // {
        //     PlaySound("CubeRespawn", transform);
        // }

         private void OnEnable()
         {
             // GameEvents.Intro += OnIntro;
             // GameEvents.GameStarted += OnGameStart;
             // GameEvents.GameOver += EndGame;
             EventManager.StartListening(EventManager.GameEvent.CubeRespawnSound, OnCubeRespawnSound);

         }

         private void OnDisable()
         {
             // GameEvents.Intro -= OnIntro;
             // GameEvents.GameStarted -= OnGameStart;
             // GameEvents.GameOver -= EndGame;
             EventManager.StopListening(EventManager.GameEvent.CubeRespawnSound, OnCubeRespawnSound);

         }

        private void EndGame()
        {
            StopAllSounds();
            //PlaySound("LongBeep", transform);
        }

        private void OnIntro()
        {
            StopAllSounds();
            //PlaySound("Intro", transform);
        }
        
        private void OnGameStart()
        {
            StopAllSounds();
            //PlaySound("BackGround", transform);
        }

        private readonly HashSet<AudioSourceWrapper> activeSounds = new HashSet<AudioSourceWrapper>();

        public void PlaySound(string audioName, Transform spawnTransform, float customVolume = -1f)
        {
            var config = FindAudioConfig(audioName);
            if (config == null)
                return;
            var soundObject = SoundPool.Instance.Get();
            soundObject.transform.position = spawnTransform.position;
            float finalVolume = (customVolume >= 0f) ? customVolume : config.volume;
            soundObject.Play(config.clip, finalVolume, config.loop);
            
            activeSounds.Add(soundObject);
            StartCoroutine(WaitAndRemove(soundObject));
            
        }
        
        private IEnumerator WaitAndRemove(AudioSourceWrapper wrapper)
        {
            while (wrapper != null && wrapper.IsPlaying())
            {
                yield return null;
            }

            activeSounds.Remove(wrapper);
        }
        
        public void StopAllSounds()
        {
            foreach (var sound in activeSounds)
            {
                sound.Reset(); 
                sound.gameObject.SetActive(false);
                SoundPool.Instance.Return(sound);
            }

            activeSounds.Clear();
        }

        private AudioConfig FindAudioConfig(string audioName)
        {
            var x = settings.audioConfigs.FirstOrDefault(config => config.name == audioName);
            if(x!= null)
            {
                return x;
            }
            else
            {
                Debug.LogError($"Audio config not found for {audioName}");
                return null;
            }
        }
        
        private void OnCubeRespawnSound(object _)
        {
            PlaySound("CubeRespawn", transform);
        }

    }
}