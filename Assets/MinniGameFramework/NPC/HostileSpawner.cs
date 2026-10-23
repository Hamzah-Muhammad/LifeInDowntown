using System.Collections.Generic;
using MinniGameFramework.Combat;
using MinniGameFramework.Core;
using MinniGameFramework.Player;
using UnityEngine;

namespace MinniGameFramework.NPC
{
    // Puts armed people on the street for a mission objective (docs/DESIGN.md 3P.4): `count` of them on the
    // sidewalk near a place, a couple of metres apart, loitering until something sets them off. The
    // objective's "enemies" field asks for them (MissionManager); NpcSetup fills in the characters and the pistol.
    public class HostileSpawner : MonoBehaviour
    {
        public static HostileSpawner Instance { get; private set; }
        public static HostileSpawner Find() => Instance != null ? Instance : (Instance = FindFirstObjectByType<HostileSpawner>());

        public GameObject[] prefabs;     // Rocketbox crowd men (never Ty or Tarik)
        public GameObject pistol;

        void Awake() { Instance = this; }

        public List<Hostile> Spawn(Vector3 place, int count, float spread = 8f)
        {
            var spots = Spots(place, count, spread);
            var made = new List<Hostile>();
            for (int i = 0; i < spots.Count && prefabs != null && prefabs.Length > 0; i++)
            {
                var prefab = prefabs[Random.Range(0, prefabs.Length)];
                Vector3 face = place - spots[i]; face.y = 0f;
                var go = Instantiate(prefab, spots[i], face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-face) : Quaternion.identity);   // backs to the middle, watching the street
                go.name = $"Hostile_{i}";
                var ped = go.GetComponent<Pedestrian>();
                if (ped != null) { ped.path = null; ped.mode = Pedestrian.Mode.TalkCool; }
                var h = go.AddComponent<Hostile>();
                h.pistolModel = pistol;
                h.Init();
                made.Add(h);
            }
            Debug.Log($"HOSTILES spawned {made.Count} at {place}");
            return made;
        }

        // sidewalk points near the place (FrameworkHooks.SidewalkPoints, the game's crowd lines), at least 2.5 m apart; without a crowd, rings on the ground
        static List<Vector3> Spots(Vector3 place, int count, float spread)
        {
            var spots = new List<Vector3>();
            var sidewalk = FrameworkHooks.SidewalkPoints();
            if (sidewalk != null && sidewalk.Count > 0)
            {
                var near = new List<Vector3>();
                foreach (var p in sidewalk) if (Flat(p - place).magnitude < spread * 3f) near.Add(p);
                near.Sort((a, b) => Flat(a - place).sqrMagnitude.CompareTo(Flat(b - place).sqrMagnitude));
                foreach (var p in near)
                {
                    if (spots.Count >= count) break;
                    if (spots.TrueForAll(s => Flat(s - p).magnitude >= 2.5f)) spots.Add(p);
                }
            }
            for (int i = 0; spots.Count < count && i < count * 4; i++)
            {
                Vector3 p = place + Quaternion.Euler(0f, i * 360f / (count * 2), 0f) * Vector3.forward * Mathf.Min(spread, 2.5f + i * 0.8f);
                if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var g, 6f, ~0, QueryTriggerInteraction.Ignore)) p.y = g.point.y; else p.y = place.y;
                if (spots.TrueForAll(s => Flat(s - p).magnitude >= 2.5f)) spots.Add(p);
            }
            return spots;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
