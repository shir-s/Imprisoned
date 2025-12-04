using System.Collections.Generic;
using UnityEngine;

namespace JellyGame.GamePlay.Traps.FallingTrap
{
    public class TrapPressureZone : MonoBehaviour
    {
        [SerializeField] private LayerMask enemyLayers;
        [SerializeField] private FallTrap crusher;    // referance to top crusher

        private HashSet<GameObject> enemiesInside = new HashSet<GameObject>();

        private void OnTriggerEnter(Collider other)
        {
            if ((enemyLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            enemiesInside.Add(other.gameObject);

            if (enemiesInside.Count >= 2)
                crusher.Activate(enemiesInside); //activate crusher when 2 or more enemies are inside
        }

        private void OnTriggerExit(Collider other)
        {
            if (enemiesInside.Contains(other.gameObject))
                enemiesInside.Remove(other.gameObject);
        }
    }
}