using System.Collections;
using System.Collections.Generic;
using JellyGame.GamePlay.Enemy;
using UnityEngine;

namespace JellyGame.GamePlay.Traps.FallingTrap
{
    public class FallTrap : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private Transform crusherTop;    //top part of the crusher
        [SerializeField] private float crushY = 0.5f;      // Y position to crush down to
        [SerializeField] private float speed = 3f;
        [SerializeField] private float stayDownTime = 0.5f;

        private Vector3 startPos;
        
        [SerializeField] private bool killEnemyInstantly = true;
        [SerializeField] private int enemyDamage = 50;
        private void Awake()
        {
            startPos = crusherTop.position;
        }

        public void Activate(HashSet<GameObject> enemiesToCrush)
        {
            StartCoroutine(CrushRoutine(enemiesToCrush));
        }

        private IEnumerator CrushRoutine(HashSet<GameObject> enemies)
        {
            // going down
            Vector3 targetPos = new Vector3(startPos.x, crushY, startPos.z);

            while (Vector3.Distance(crusherTop.position, targetPos) > 0.01f)
            {
                crusherTop.position = Vector3.MoveTowards(
                    crusherTop.position,
                    targetPos,
                    speed * Time.deltaTime
                );
                yield return null;
            }

            // מחיקת אויבים שנמצאים בזון
            foreach (var e in enemies)
            {
                var enemyHealth = e.GetComponentInParent<EnemyHealth>();
                if (enemyHealth != null && enemyHealth.CurrentHealth > 0)
                {
                    if (killEnemyInstantly)
                        enemyHealth.Kill();
                    else
                        enemyHealth.TakeDamage(enemyDamage);
                }
            }

            // השהייה למטה
            yield return new WaitForSeconds(stayDownTime);

            // עלייה בחזרה
            while (Vector3.Distance(crusherTop.position, startPos) > 0.01f)
            {
                crusherTop.position = Vector3.MoveTowards(
                    crusherTop.position,
                    startPos,
                    speed * Time.deltaTime
                );
                yield return null;
            }
        }
    }
}