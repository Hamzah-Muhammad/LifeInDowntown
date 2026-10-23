using LifeInDowntown.Game;
using LifeInDowntown.NPC;
using UnityEngine;

namespace LifeInDowntown.Phone
{
    // The story characters in person (docs/DESIGN.md 3O.1). When the active objective says someone is
    // waiting ("npc": "ty"), they stand at the meeting place, snapped to the nearest real sidewalk, on
    // their phone, turning to watch Tarik as he comes up. Gone again once that objective is done.
    public class Cast : MonoBehaviour
    {
        public MissionManager missions;
        public GameObject tyPrefab;            // Rocketbox Male_Adult_10, kept out of the street crowd

        GameObject waiting;
        string waitingFor;                     // mission#objective the person is standing for

        public Transform Waiting => waiting != null ? waiting.transform : null;

        void Update()
        {
            var obj = missions != null ? missions.ActiveObjective : null;
            string key = obj != null && !string.IsNullOrEmpty(obj.npc) ? $"{GameState.Current.activeMission}#{GameState.Current.objectiveIndex}" : null;
            if (key == waitingFor) return;
            if (waiting != null) Destroy(waiting);
            waiting = null;
            waitingFor = key;
            missions.SetMeetPoint(null);
            if (key != null && missions.PlaceTarget.HasValue) Spawn(obj.npc, missions.PlaceTarget.Value);
            if (waiting != null) missions.SetMeetPoint(waiting.transform.position);
        }

        void Spawn(string who, Vector3 place)
        {
            var prefab = who == "ty" ? tyPrefab : null;
            if (prefab == null) return;
            Vector3 at = CrowdDirector.Instance != null ? CrowdDirector.Instance.NearestSidewalk(place) : place;
            waiting = Instantiate(prefab, at, Quaternion.LookRotation(Flat(place - at).sqrMagnitude > 0.01f ? Flat(place - at) : Vector3.forward));
            waiting.name = who == "ty" ? "Ty" : who;
            var p = waiting.GetComponent<Pedestrian>();
            if (p != null) { p.path = null; p.role = Pedestrian.Role.Story; p.mode = Pedestrian.Mode.Phone; p.watchPlayer = true; p.watchRange = 14f; }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
