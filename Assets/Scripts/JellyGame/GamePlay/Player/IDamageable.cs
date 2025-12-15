// FILEPATH: Assets/Scripts/Combat/IDamageable.cs
namespace JellyGame.GamePlay.Combat
{
    /// <summary>
    /// Universal interface for anything that can take damage and/or be healed.
    /// 
    /// IMPORTANT:
    /// - Damage and heal are abstract units.
    /// - The receiver decides what they mean (HP, size, armor, etc.).
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(float amount);
        void Heal(float amount);
    }
}