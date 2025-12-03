// FILEPATH: Assets/Scripts/AI/IEnemySound.cs
using UnityEngine;

/// <summary>
/// Optional interface for behaviors that want to control sound playback.
/// 
/// Behaviors can implement this alongside IEnemyBehavior to add sound control.
/// The AICharacterSound component will call these methods when appropriate.
/// 
/// Example usage:
/// public class WanderBehavior : MonoBehaviour, IEnemyBehavior, IEnemySound
/// {
///     // IEnemyBehavior implementation...
///     
///     // IEnemySound implementation:
///     public SoundPlaybackMode GetSoundMode() => SoundPlaybackMode.FixedInterval;
///     public float GetSoundInterval() => 2f;
///     public string GetSoundName() => "EnemyWander";
/// }
/// </summary>
public interface IEnemySound
{
    /// <summary>
    /// How should sound be played while this behavior is active?
    /// </summary>
    SoundPlaybackMode GetSoundMode();

    /// <summary>
    /// For FixedInterval and RandomInterval modes: interval in seconds.
    /// For RandomInterval: this is the MIN interval (max is GetMaxSoundInterval).
    /// </summary>
    float GetSoundInterval();

    /// <summary>
    /// For RandomInterval mode only: maximum interval in seconds.
    /// If not implemented, defaults to GetSoundInterval() * 2.
    /// </summary>
    float GetMaxSoundInterval() => GetSoundInterval() * 2f;

    /// <summary>
    /// Name of the sound to play (must match an entry in AudioSettings).
    /// Can return null to not play any sound for this behavior.
    /// </summary>
    string GetSoundName();

    /// <summary>
    /// Optional: Custom volume for this sound (0-1).
    /// Return -1 to use the default volume from AudioSettings.
    /// </summary>
    float GetSoundVolume() => -1f;

    /// <summary>
    /// Optional: Called when sound is about to play.
    /// Return false to skip playing this time.
    /// Useful for conditional sound playback.
    /// </summary>
    bool ShouldPlaySound() => true;
}

/// <summary>
/// Defines how sound should be played for a behavior.
/// </summary>
public enum SoundPlaybackMode
{
    /// <summary>No sound played.</summary>
    None,

    /// <summary>Sound plays at fixed intervals (e.g., every 2 seconds).</summary>
    FixedInterval,

    /// <summary>Sound plays at random intervals between min and max.</summary>
    RandomInterval,

    /// <summary>Sound loops continuously while behavior is active.</summary>
    Loop,

    /// <summary>Sound plays once when behavior enters, then stops.</summary>
    OnEnter,

    /// <summary>Sound plays once when behavior exits.</summary>
    OnExit,

    /// <summary>Sound plays on enter and loops until exit.</summary>
    OnEnterLoop
}