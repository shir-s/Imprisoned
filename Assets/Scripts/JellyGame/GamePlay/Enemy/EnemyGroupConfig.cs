using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using JellyGame.GamePlay.Utils;

namespace JellyGame.GamePlay.Enemy
{
    [Serializable]
    public class EnemyGroupConfig
    {
        [Tooltip("שם נקי בשבילך לאינספקטור בלבד")]
        public string groupName;

        [Tooltip("האויבים בקבוצה הזו")]
        public List<EnemyHealth> enemies = new List<EnemyHealth>();

        [Tooltip("האם כשהקבוצה הזו מתה – נרים JellyGameEvents.FirstEnemyDied (פעם אחת בלבד)")]
        public bool raisesFirstEnemyDied;

        [Tooltip("האם האויבים בקבוצה הזו נספרים לצורך JellyGameEvents.AllEnemiesDied")]
        public bool countTowardsAll = true;

        [Tooltip("איוונט שנקרא כשהקבוצה הזו ריקה (כל האויבים שלה מתו)")]
        public UnityEvent onGroupCleared;
    }
}
