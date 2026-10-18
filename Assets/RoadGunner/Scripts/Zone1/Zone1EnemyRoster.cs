using System;
using UnityEngine;

namespace RoadGunner
{
    /// <summary>Explicit scene roster. Bosses reset only with the run, never through ordinary respawn pools.</summary>
    [DisallowMultipleComponent]
    public sealed class Zone1EnemyRoster : MonoBehaviour
    {
        public GameSession session;
        public CombatTarget[] bosses = Array.Empty<CombatTarget>();
        public CombatTarget[] ExtraTargets => bosses;
        public int ActiveBossCount
        {
            get
            {
                int count = 0;
                if (bosses != null) for (int i = 0; i < bosses.Length; i++) if (bosses[i] && bosses[i].Alive) count++;
                return count;
            }
        }
        public void ResetEncounters()
        {
            if (bosses == null) return;
            for (int i = 0; i < bosses.Length; i++)
            {
                var target = bosses[i]; if (!target) continue;
                if (target is WastelandMutant mutant) { mutant.session = session; mutant.ResetEncounter(); }
                else if (target is TruckArenaBoss truck) { truck.session = session; truck.ResetEncounter(); }
            }
        }
        public void CancelAttacks()
        {
            if (bosses == null) return;
            for (int i = 0; i < bosses.Length; i++)
            {
                var target = bosses[i]; if (!target) continue;
                if (target is WastelandMutant mutant) mutant.CancelAttack();
                else if (target is TruckArenaBoss truck) truck.CancelAttack();
            }
        }
        public void DespawnAll()
        {
            if (bosses == null) return;
            for (int i = 0; i < bosses.Length; i++)
            {
                var target = bosses[i]; if (!target) continue;
                if (target is WastelandMutant mutant) mutant.Despawn();
                else if (target is TruckArenaBoss truck) truck.Despawn();
            }
        }
        void OnDisable() { CancelAttacks(); }
    }
}
