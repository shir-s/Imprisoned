using System;
using UnityEngine;

namespace JellyGame.GamePlay.Audio.Core
{
    [CreateAssetMenu(fileName = "AudioSettings", menuName = "Audio/AudioSettings")]
    public class AudioSettings : ScriptableObject
    {
        public AudioConfig[] audioConfigs;
    }

    [Serializable]
    public class AudioConfig
    {
        public string name;
        public AudioClip clip;
        public float volume = 1f;
        public bool loop = false;
    }
}