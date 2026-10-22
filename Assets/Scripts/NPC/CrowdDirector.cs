using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.NPC
{
    // The street crowd (docs/DESIGN.md 3L), GTA-style: a pool of walkers kept around the player on the real
    // sidewalk lines (baked by NpcSetup for the whole map), spawned out of view and recycled once they fall
    // behind. The placed street people (corners, crosswalks, homeless spots) switch off when far away.
    // Upstairs in the condo (y > 60) the whole crowd sleeps.
    public class CrowdDirector : MonoBehaviour
    {
        public GameObject[] walkerPrefabs;
        public GameObject[] policePrefabs;    // a couple of officers walk the beat among the walkers
        public Pedestrian[] statics;          // placed by NpcSetup
        [Header("Sidewalk lines (flat)")]
        public Vector3[] points;              // all lines back to back
        public int[] lineStart;               // first point of each line; the line ends where the next starts
        public float[] lineWeight;            // Yonge St 3, primary 2, ... (busier streets get more people)
        [Header("Tuning")]
        public int walkers = 90;
        public int policeWalkers = 2;         // part of the pool, not extra
        public float spawnMin = 35f, spawnMax = 140f, despawn = 165f, staticRange = 150f;
        public float slowWalkChance = 0.2f;

        const float Cell = 40f;
        readonly List<Pedestrian> pool = new List<Pedestrian>();
        readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();   // cell -> point indices
        readonly List<int> candidates = new List<int>();
        readonly List<float> cumulative = new List<float>();
        int[] lineOf;
        Transform player;
        Camera cam;
        bool asleep, filled;
        float nextTick, statWindow, statFrames, nextStat = 15f;

        void Start()
        {
            var body = FindFirstObjectByType<LifeInDowntown.Player.BodycamPlayer>();
            player = body != null ? body.transform : null;
            Pedestrian.Player = player;
            lineOf = new int[points.Length];
            for (int l = 0; l < lineStart.Length; l++)
                for (int i = lineStart[l]; i < End(l); i++) lineOf[i] = l;
            for (int i = 0; i < points.Length; i++)
            {
                var key = Key(points[i]);
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }
            var rnd = new System.Random(77);
            for (int n = 0; n < walkers && walkerPrefabs.Length > 0; n++)
            {
                bool cop = n < policeWalkers && policePrefabs != null && policePrefabs.Length > 0;
                var go = Instantiate(cop ? policePrefabs[rnd.Next(policePrefabs.Length)] : walkerPrefabs[rnd.Next(walkerPrefabs.Length)], transform);
                go.name = $"Walker_{n:00}";
                go.SetActive(false);
                pool.Add(go.GetComponent<Pedestrian>());
            }
            Debug.Log($"CROWD_OK pool={pool.Count} statics={statics.Length} lines={lineStart.Length} points={points.Length}");
        }

        int End(int line) => line + 1 < lineStart.Length ? lineStart[line + 1] : points.Length;
        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        void Update()
        {
            statWindow += Time.unscaledDeltaTime; statFrames++;
            if (Time.unscaledTime > nextStat)
            {
                Debug.Log($"CROWD walkers={ActiveWalkers()} statics={ActiveStatics()} fps={statFrames / Mathf.Max(statWindow, 1e-3f):F0}");
                statWindow = statFrames = 0f; nextStat = Time.unscaledTime + 15f;
            }
            if (player == null || Time.time < nextTick) return;
            nextTick = Time.time + 0.4f;
            if (cam == null) cam = Camera.main;

            bool up = player.position.y > 60f;
            if (up != asleep) { asleep = up; Sleep(up); }
            if (asleep) return;

            Vector3 at = player.position;
            foreach (var s in statics)
                if (s != null) s.gameObject.SetActive(FlatDist(s.transform.position, at) < staticRange);
            foreach (var p in pool)
                if (p.gameObject.activeSelf && FlatDist(p.transform.position, at) > despawn) p.gameObject.SetActive(false);

            // first fill (new game, or back down from the condo): anywhere in range, as if they were already there
            int budget = filled ? 3 : walkers;
            foreach (var p in pool)
            {
                if (budget <= 0) break;
                if (p.gameObject.activeSelf) continue;
                if (!Spawn(p, at, !filled)) break;
                budget--;
            }
            filled = true;
        }

        void Sleep(bool on)
        {
            foreach (var p in pool) p.gameObject.SetActive(false);
            foreach (var s in statics) if (s != null) s.gameObject.SetActive(!on);
            filled = false;
        }

        bool Spawn(Pedestrian p, Vector3 at, bool anywhere)
        {
            Gather(at, anywhere ? 8f : spawnMin);
            for (int attempt = 0; attempt < 12 && candidates.Count > 0; attempt++)
            {
                int idx = Pick();
                Vector3 pos = points[idx];
                if (!anywhere && InView(pos)) continue;            // pop in where the camera isn't looking
                int line = lineOf[idx], s0 = lineStart[line], s1 = End(line);
                if (s1 - s0 < 2) continue;
                float lane = Random.Range(-0.6f, 0.6f);             // spread people across the sidewalk
                var path = new Vector3[s1 - s0];
                for (int i = 0; i < path.Length; i++) path[i] = points[s0 + i] + Side(s0, s1, s0 + i) * lane;
                p.gameObject.SetActive(true);
                p.Begin(path, idx - s0, Random.value < 0.5f ? 1 : -1, Random.value < slowWalkChance ? Pedestrian.Mode.WalkSlow : Pedestrian.Mode.Walk);
                return true;
            }
            return false;
        }

        // sidewalk points in the spawn ring, weighted by how busy the street is
        void Gather(Vector3 at, float min)
        {
            candidates.Clear(); cumulative.Clear();
            var c = Key(at);
            int r = Mathf.CeilToInt(spawnMax / Cell);
            float total = 0f;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (!grid.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        float d = FlatDist(points[i], at);
                        if (d < min || d > spawnMax) continue;
                        candidates.Add(i); total += lineWeight[lineOf[i]]; cumulative.Add(total);
                    }
                }
        }

        int Pick()
        {
            float x = Random.value * cumulative[cumulative.Count - 1];
            int lo = 0, hi = cumulative.Count - 1;
            while (lo < hi) { int m = (lo + hi) / 2; if (cumulative[m] < x) lo = m + 1; else hi = m; }
            return candidates[lo];
        }

        bool InView(Vector3 pos)
        {
            if (cam == null || FlatDist(pos, cam.transform.position) > 90f) return false;
            var v = cam.WorldToViewportPoint(pos + Vector3.up);
            return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }

        Vector3 Side(int s0, int s1, int i)
        {
            var d = points[Mathf.Min(s1 - 1, i + 1)] - points[Mathf.Max(s0, i - 1)]; d.y = 0f; d.Normalize();
            return new Vector3(-d.z, 0f, d.x);
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        public int ActiveWalkers() { int n = 0; foreach (var p in pool) if (p.gameObject.activeSelf) n++; return n; }
        public int ActiveStatics() { int n = 0; foreach (var s in statics) if (s != null && s.gameObject.activeSelf) n++; return n; }
    }
}
