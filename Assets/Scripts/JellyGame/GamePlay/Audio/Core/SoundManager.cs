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
        
        private AudioSourceWrapper _currentBackgroundMusic;
        private string _currentMusicName;

        private readonly HashSet<AudioSourceWrapper> activeSounds = new HashSet<AudioSourceWrapper>();

        private void OnEnable()
        {
             EventManager.StartListening(EventManager.GameEvent.CubeRespawnSound, OnCubeRespawnSound);
        }

        private void OnDisable()
        {
             EventManager.StopListening(EventManager.GameEvent.CubeRespawnSound, OnCubeRespawnSound);
        }

         public AudioSourceWrapper PlaySound(string audioName, Transform spawnTransform, float customVolume = -1f)
        {
            var config = FindAudioConfig(audioName);
            if (config == null) return null;

            var soundObject = SoundPool.Instance.Get();
            soundObject.transform.position = spawnTransform.position;
            
            float finalVolume = (customVolume >= 0f) ? customVolume : config.volume;
            soundObject.Play(config.clip, finalVolume, config.loop);
    
            activeSounds.Add(soundObject);
            
            StartCoroutine(WaitAndRemove(soundObject));
    
            return soundObject; 
        }
         private AudioSourceWrapper PlayLoopingSound(string audioName, Transform spawnTransform)
        {
            var config = FindAudioConfig(audioName);
            if (config == null) return null;

            var soundObject = SoundPool.Instance.Get();
            soundObject.transform.position = spawnTransform.position;

            soundObject.Play(config.clip, config.volume, true); // Force Loop

            activeSounds.Add(soundObject);
            return soundObject;
        }
          public void PlayBackgroundMusic(string audioName)
        {
            if (_currentBackgroundMusic != null && 
                _currentBackgroundMusic.IsPlaying() && 
                _currentMusicName == audioName)
            {
                return;
            }

            StopBackgroundMusic();

            _currentBackgroundMusic = PlayLoopingSound(audioName, transform);
            _currentMusicName = audioName;
        }
        
        public void StopBackgroundMusic()
        {
            if (_currentBackgroundMusic != null)
            {
                if (activeSounds.Contains(_currentBackgroundMusic))
                {
                    activeSounds.Remove(_currentBackgroundMusic);
                }

                _currentBackgroundMusic.Reset();
                _currentBackgroundMusic.gameObject.SetActive(false);
                SoundPool.Instance.Return(_currentBackgroundMusic);
        
                _currentBackgroundMusic = null;
                _currentMusicName = "";
            }
        }

        public void StopAllSounds()
        {
            foreach (var sound in activeSounds.ToList())
            {
                if (sound == null) continue;

                sound.Reset(); 
                if (sound.gameObject != null) sound.gameObject.SetActive(false);
                SoundPool.Instance.Return(sound);
            }

            activeSounds.Clear();
            
            _currentBackgroundMusic = null;
            _currentMusicName = "";
        }

        private IEnumerator WaitAndRemove(AudioSourceWrapper wrapper)
        {
            while (wrapper != null && wrapper.IsPlaying())
            {
                yield return null;
            }

            if (wrapper != null)
            {
                activeSounds.Remove(wrapper);
            }
        }

        public AudioConfig FindAudioConfig(string audioName)
        {
            if (settings == null) return null;
            return settings.audioConfigs.FirstOrDefault(config => config.name == audioName);
        }
        
        private void OnCubeRespawnSound(object _)
        {
            PlaySound("CubeRespawn", transform);
        }
    }
}