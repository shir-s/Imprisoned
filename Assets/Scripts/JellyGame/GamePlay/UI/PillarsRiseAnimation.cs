// FILEPATH: Assets/Scripts/JellyGame/GamePlay/World/PillarsYMover.cs
using System.Collections.Generic;
using UnityEngine;
using JellyGame.GamePlay.Managers;

namespace JellyGame.GamePlay.World
{
    public class PillarsYMover : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CountdownTimer countdownTimer;
        
        [Tooltip("The pillars to move. They must be children of the platform.")]
        [SerializeField] private List<Transform> pillars;

        [Header("Y Axis Settings (Local)")]
        [Tooltip("The Y position when time is FULL (Start of level). Usually lower.")]
        [SerializeField] private float startLocalY = -5f;

        [Tooltip("The Y position when time is ZERO (End of level). Usually higher (0 means flush with parent).")]
        [SerializeField] private float endLocalY = 0f;

        [Header("Debug")]
        [Tooltip("Show the movement path in the Scene view?")]
        [SerializeField] private bool showGizmos = true;

        private float _totalTime;

        // We store the X and Z for each pillar so we don't change them, only Y.
        private List<Vector2> _pillarsXZ = new List<Vector2>();

        private void Start()
        {
            if (countdownTimer == null)
                countdownTimer = FindObjectOfType<CountdownTimer>();

            if (countdownTimer != null)
            {
                _totalTime = Mathf.Max(1f, countdownTimer.RemainingSeconds);
            }
            
            // Capture the original X and Z of each pillar
            foreach (var pillar in pillars)
            {
                if (pillar != null)
                {
                    _pillarsXZ.Add(new Vector2(pillar.localPosition.x, pillar.localPosition.z));
                }
                else
                {
                    // Keep list synced even if null to avoid index errors
                    _pillarsXZ.Add(Vector2.zero); 
                }
            }
        }

        private void Update()
        {
            if (countdownTimer == null) return;

            // 1. Calculate Progress (0 = Start, 1 = End)
            float currentRemaining = countdownTimer.RemainingSeconds;
            float percentagePassed = 1f - Mathf.Clamp01(currentRemaining / _totalTime);

            // 2. Calculate the current Y height based on progress
            float currentY = Mathf.Lerp(startLocalY, endLocalY, percentagePassed);

            // 3. Apply to all pillars
            for (int i = 0; i < pillars.Count; i++)
            {
                if (pillars[i] != null)
                {
                    // We use the stored X and Z, and the new calculated Y
                    Vector3 newPos = new Vector3(_pillarsXZ[i].x, currentY, _pillarsXZ[i].y); // Note: stored Y is actually Z in 3D
                    
                    pillars[i].localPosition = newPos;
                }
            }
        }

        // This draws lines in the Editor so you can see the path BEFORE playing
        private void OnDrawGizmos()
        {
            if (!showGizmos || pillars == null) return;

            Gizmos.color = Color.yellow;
            foreach (var pillar in pillars)
            {
                if (pillar == null) continue;

                // Determine parent logic for preview
                Transform parent = pillar.parent;
                Vector3 parentPos = parent != null ? parent.position : Vector3.zero;
                
                // Calculate world positions for preview (approximation)
                // Note: accurate preview requires running game, but this gives a general idea
                // if the parent is at (0,0,0) or if we just look at local offset directions.
                
                // Better visualization: Draw lines relative to the pillar itself
                // We assume the pillar is currently at one of the positions.
                
                Vector3 bottomPoint = pillar.parent.TransformPoint(new Vector3(pillar.localPosition.x, startLocalY, pillar.localPosition.z));
                Vector3 topPoint = pillar.parent.TransformPoint(new Vector3(pillar.localPosition.x, endLocalY, pillar.localPosition.z));

                Gizmos.DrawLine(bottomPoint, topPoint);
                Gizmos.DrawSphere(bottomPoint, 0.1f);
                Gizmos.DrawSphere(topPoint, 0.1f);
            }
        }
    }
}