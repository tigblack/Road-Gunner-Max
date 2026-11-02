using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoadGunner.Editor
{
    /// <summary>
    /// Read-only RENDER look-dev dump (never saves): renderers around the workshop and stations, enemy/player actor
    /// renderers and their materials. -executeMethod RoadGunner.Editor.ArcadeVisualDiagnostics.DumpBatch
    /// Writes outputs/av/render-dump.txt.
    /// </summary>
    public static class ArcadeVisualDiagnostics
    {
        public static void DumpBatch()
        {
            var session = ArcadeFeedbackSetup.OpenGameplayScene();
            var text = new StringBuilder();
            var renderers = RoadGunnerLevelAuthoring.InScene<Renderer>().Where(r => r).ToArray();

            foreach (var workshop in RoadGunnerLevelAuthoring.InScene<RepairWorkshop>().Where(w => w))
            {
                text.AppendLine("=== WORKSHOP " + Path(workshop.transform) + " at " + workshop.transform.position + " rot " + workshop.transform.eulerAngles + " radius " + workshop.healingRadius);
                foreach (var r in renderers.Where(r => Flat(r.bounds.center - workshop.transform.position) < 19f).OrderBy(r => Path(r.transform)))
                    text.AppendLine(Describe(r));
                foreach (var c in workshop.GetComponentsInChildren<Collider>(true))
                    text.AppendLine("  COLLIDER " + Path(c.transform) + " " + c.GetType().Name + " enabled=" + c.enabled + " trigger=" + c.isTrigger);
            }
            foreach (var station in RoadGunnerLevelAuthoring.InScene<ArenaUpgradeStation>().Where(s => s))
            {
                text.AppendLine("=== STATION " + station.kind + " " + Path(station.transform) + " at " + station.transform.position);
                foreach (var r in station.GetComponentsInChildren<Renderer>(true)) text.AppendLine(Describe(r));
            }
            text.AppendLine("=== ENEMY / ACTOR RENDERERS");
            foreach (var raider in RoadGunnerLevelAuthoring.InScene<Raider>().Where(x => x).Take(3))
            { text.AppendLine("RAIDER " + Path(raider.transform) + " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(raider)); foreach (var r in raider.GetComponentsInChildren<Renderer>(true)) text.AppendLine(Describe(r)); }
            foreach (var buggy in RoadGunnerLevelAuthoring.InScene<EnemyBuggy>().Where(x => x).Take(1))
            { text.AppendLine("BUGGY " + Path(buggy.transform) + " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(buggy)); foreach (var r in buggy.GetComponentsInChildren<Renderer>(true)) text.AppendLine(Describe(r)); }
            foreach (var mutant in RoadGunnerLevelAuthoring.InScene<WastelandMutant>().Where(x => x).Take(1))
            { text.AppendLine("MUTANT " + Path(mutant.transform) + " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mutant)); foreach (var r in mutant.GetComponentsInChildren<Renderer>(true)) text.AppendLine(Describe(r)); }
            text.AppendLine("Raiders in scene: " + RoadGunnerLevelAuthoring.InScene<Raider>().Count(x => x) + ", buggies " + RoadGunnerLevelAuthoring.InScene<EnemyBuggy>().Count(x => x) + ", mutants " + RoadGunnerLevelAuthoring.InScene<WastelandMutant>().Count(x => x));
            foreach (var group in RoadGunnerLevelAuthoring.InScene<Raider>().Where(x => x).GroupBy(x => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(x) ?? "(not prefab)"))
                text.AppendLine("  raider prefab " + group.Key + " x" + group.Count());
            if (session && session.vehicle)
            {
                text.AppendLine("PLAYER " + Path(session.vehicle.transform));
                foreach (var r in session.vehicle.GetComponentsInChildren<Renderer>(true)) text.AppendLine(Describe(r));
            }
            text.AppendLine("=== TALL SHADOW CASTERS (> 6 m high, shadows on)");
            foreach (var r in renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy && r.shadowCastingMode != ShadowCastingMode.Off && r.bounds.size.y > 6).OrderByDescending(r => r.bounds.size.y).Take(25))
                text.AppendLine(Describe(r));
            text.AppendLine("=== TRANSPARENT / QUEUE >= 2450 RENDERERS near workshop");
            foreach (var r in renderers.Where(r => r.sharedMaterials.Any(m => m && m.renderQueue >= 2450)).Take(80))
                text.AppendLine(Describe(r));

            string path = System.IO.Path.Combine(Directory.GetParent(Application.dataPath).FullName, "outputs", "av", "render-dump.txt");
            File.WriteAllText(path, text.ToString());
            Debug.Log("ARCADE VISUAL DUMP OK " + path);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        public static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Describe(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            string mesh = filter && filter.sharedMesh ? filter.sharedMesh.name + "(" + filter.sharedMesh.vertexCount + "v)" : r.GetType().Name;
            string mats = string.Join(" | ", r.sharedMaterials.Select(m => m ? m.name + "[" + (m.shader ? m.shader.name : "?") + " q" + m.renderQueue +
                (m.HasProperty("_BaseColor") ? " #" + ColorUtility.ToHtmlStringRGBA(m.GetColor("_BaseColor")) : "") + (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? " tex=" + m.GetTexture("_BaseMap").name : "") + "]" : "null"));
            var b = r.bounds;
            return "  " + Path(r.transform) + " :: " + mesh + " en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy + " shadow=" + r.shadowCastingMode +
                " recv=" + r.receiveShadows + " static=" + r.gameObject.isStatic + " c=" + b.center.ToString("F2") + " s=" + b.size.ToString("F2") + " lpos=" + r.transform.localPosition.ToString("F3") + " :: " + mats;
        }
    }
}
