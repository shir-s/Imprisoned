using System;
using UnityEngine;

namespace JellyGame.GamePlay.Utils
{
    public static class JellyGameEvents
    {
        public static Action FirstEnemyDied;
        public static Action AllEnemiesDied;
        public static Action<Vector3> EnemyDied;
    }
}