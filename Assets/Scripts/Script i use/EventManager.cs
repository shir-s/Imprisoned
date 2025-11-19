// FILEPATH: Assets/Scripts/Core/EventManager.cs
using UnityEngine;
using System;
using System.Collections.Generic;

public delegate void EventAction(object eventData);

public static class EventManager
{
    private static Dictionary<GameEvent, EventAction> eventTable =
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
}