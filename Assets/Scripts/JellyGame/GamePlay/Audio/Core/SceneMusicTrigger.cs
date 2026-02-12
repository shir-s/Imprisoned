using UnityEngine;
using JellyGame.GamePlay.Audio.Core; 

namespace JellyGame.GamePlay.Audio
{
    public class SceneMusicTrigger : MonoBehaviour
    {
        [Tooltip("The exact name of the music clip from AudioSettings")]
        [SerializeField] private string musicName;

        private void Start()
        {
            if (!string.IsNullOrEmpty(musicName))
            {
                SoundManager.Instance.PlayBackgroundMusic(musicName);
            }
            else
            {
                SoundManager.Instance.StopBackgroundMusic();
            }
        }
    }
}