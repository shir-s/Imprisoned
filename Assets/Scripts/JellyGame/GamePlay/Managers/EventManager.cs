// FILEPATH: Assets/Scripts/Core/EventManager.cs
using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Managers
{
    public delegate void EventAction(object eventData);

    public static class EventManager
    {
        private static readonly Dictionary<GameEvent, EventAction> eventTable =
            new Dictionary<GameEvent, EventAction>();

        public enum GameEvent
        {
            ActiveCubeChanged,   // passes GameObject
            CubeDestroyed,       // passes GameObject
            CubeRespawned,       // passes GameObject

            CubeRespawnSound,

            // Tray / surface (optional, for later)
            TraySelected,        // data: Transform (selected tray)
            SurfaceTilted,       // data: Vector3 (current rotation / tilt info)

            // Stroke / drawing
            StrokeCrossingDetected, // data: StrokeCrossingEventData

            // NEW: fired once when a closed area is detected (even if fill is progressive)
            AreaClosed,          // data: AreaClosedEventData

            KeyCollected,        // data: Transform or GameObject (the key that was collected)

            // NPC / allies
            FriendlyNpcKilled,   // data: GameObject or Transform (the NPC that died)

            // Enemies
            EnemyKilled,         // data: GameObject or Transform (the enemy that died)

            // Universal
            EntityDied,          // data: EntityDiedEventData

            // Win/Lose
            GameWin,
            GameOver,            // data: null

            // Pickups / misc
            PickupCollected,
            PlayerDamaged,

            // Legacy JellyGameEvents equivalents
            FirstEnemyDied,      // data: null
            AllEnemiesDied,      // data: null
            EnemyDied            // data: Vector3 (enemy position)
        }

        // NEW: event payload (optional but useful)
        public struct AreaClosedEventData
        {
            public Object source;              // detector / sender (UnityEngine.Object)
            public Transform surfaceTransform; // surface transform (if known)
            public Bounds localBounds;         // bounds in surface local space (if known)
        }

        public static void StartListening(GameEvent eventType, EventAction listener)
        {
            if (eventTable.TryGetValue(eventType, out EventAction thisEvent))
            {
                thisEvent += listener;
                eventTable[eventType] = thisEvent;
            }
            else
            {
                eventTable.Add(eventType, listener);
            }
        }

        public static void StopListening(GameEvent eventType, EventAction listener)
        {
            if (eventTable.TryGetValue(eventType, out EventAction thisEvent))
            {
                thisEvent -= listener;
                if (thisEvent == null)
                {
                    eventTable.Remove(eventType);
                }
                else
                {
                    eventTable[eventType] = thisEvent;
                }
            }
        }

        public static void TriggerEvent(GameEvent eventType, object eventData = null)
        {
            if (eventTable.TryGetValue(eventType, out EventAction thisEvent))
            {
                thisEvent?.Invoke(eventData);
            }
        }

        // Convenience overloads (optional)
        public static void TriggerEvent(GameEvent eventType, Vector3 eventData)
        {
            TriggerEvent(eventType, (object)eventData);
        }
    }
}
