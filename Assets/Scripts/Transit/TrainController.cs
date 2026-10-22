using System;
using System.Collections;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Transit
{
    // One Toronto Rocket shuttling Queen <-> College in one direction (docs/DESIGN.md 3N.7). The northbound train
    // runs Queen -> TMU -> College, short-turns at College into the tunnel, and comes back from its hold point
    // south of Queen; the southbound one mirrors it. Quick travel, not a timetable:
    //   - when Tarik stands on a platform, his train is sent in from the tunnel (at most ~20 s away);
    //   - a hop accelerates at 2.2 m/s^2 to 25 m/s, cruises, brakes at 2.0 m/s^2 onto the stop mark (~30 s);
    //   - 12 s dwell, doors on the right (the platform side), real TTC wording on screen;
    //   - Enter while riding "zones out": a fade, and the train is at the next station with its doors opening.
    // The cars are moving platforms: whoever stands in a car is carried by the car's own move each step.
    [DefaultExecutionOrder(-50)]
    public class TrainController : MonoBehaviour
    {
        public enum State { Hidden, Running, Dwell, Leaving }

        public int dir = 1;                         // +1 northbound (to Finch), -1 southbound (to VMC)
        public TrainCar[] cars;                     // front first
        public float trainLength = 138.06f;
        public float maxSpeed = 25f, accel = 2.2f, brake = 2.0f;
        public float dwellSeconds = 12f, doorSeconds = 2.5f, hiddenSeconds = 8f;
        public const float HoldBack = 300f, StagingBack = 300f, FarEnough = 220f;

        public State state;
        public float headS, speed, doorOpen;
        public int stopIndex;                       // Running: the stop it heads for; Dwell: the stop it is at
        public float LastHopSeconds { get; private set; }
        public string LastHop { get; private set; } = "";
        public bool PlayerAboard { get; private set; }
        public SubwayStation[] Stops => stops;
        public string Destination => dir > 0 ? "Finch" : "Vaughan Metropolitan Centre";
        public event Action<string> Cue;            // "doors_open", "chime", "doors_close", "depart", "brake", "arrive"

        SubwayLine line;
        SubwayStation[] stops;
        BodycamPlayer player;
        CharacterController playerCc;
        float stateTime, hopStart = -1f, sayAgain;
        bool closingSaid, brakingCued, zoning;
        readonly Matrix4x4[] before = new Matrix4x4[8];
        readonly float[] beforeYaw = new float[8];

        public bool Ready => line != null && stops != null && stops.Length > 0;

        public void Init()
        {
            line = SubwayLine.Instance;
            if (line == null || line.stations == null) return;
            stops = (SubwayStation[])line.stations.Clone();
            Array.Sort(stops, (a, b) => (a.s * dir).CompareTo(b.s * dir));   // in travel order
            player = FindFirstObjectByType<BodycamPlayer>();
            if (player != null) playerCc = player.GetComponent<CharacterController>();
            ResetToHold();
        }

        public float StopHead(int i) => stops[i].s + dir * trainLength * 0.5f;
        float HoldHead => StopHead(0) - dir * HoldBack;
        float LeaveHead => StopHead(stops.Length - 1) + dir * HoldBack;
        public bool AtTerminal => stopIndex == stops.Length - 1;

        void ResetToHold()
        {
            state = State.Hidden; stateTime = 0f; headS = HoldHead; speed = 0f; stopIndex = 0; doorOpen = 0f;
            PlaceCars();
        }

        void Update()
        {
            if (!Ready) { Init(); if (!Ready) return; }
            bool enter = Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
            if (enter && CanZoneOut && !zoning) StartCoroutine(ZoneOutFade());
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (!Ready || dt <= 0f) return;
            for (int i = 0; i < cars.Length; i++) { before[i] = cars[i].transform.localToWorldMatrix; beforeYaw[i] = cars[i].transform.eulerAngles.y; }
            int carIdx = PlayerCar();
            PlayerAboard = carIdx >= 0;
            if (!PlayerAboard) Dispatch();
            stateTime += dt;

            switch (state)
            {
                case State.Hidden:
                    if (stateTime > hiddenSeconds) Depart(0);
                    break;
                case State.Running:
                    if (Drive(StopHead(stopIndex), dt)) Arrive();
                    break;
                case State.Dwell:
                    DwellStep(dt);
                    break;
                case State.Leaving:
                    if (Drive(LeaveHead, dt)) ResetToHold();
                    break;
            }
            PlaceCars();
            if (carIdx >= 0) Carry(carIdx);
            UpdateStatus();
        }

        // ------------------------------------------------------------------ motion
        // accelerate / cruise / brake so the head stops exactly on target; true on arrival
        bool Drive(float target, float dt)
        {
            float d = (target - headS) * dir;              // metres still to go
            if (d <= 0.02f) { headS = target; speed = 0f; return true; }
            float stopDist = speed * speed / (2f * brake);
            if (d <= stopDist + speed * dt)
            {
                if (!brakingCued) { brakingCued = true; Cue?.Invoke("brake"); }
                speed = Mathf.Sqrt(Mathf.Max(0f, 2f * brake * Mathf.Max(0f, d - speed * dt * 0.5f)));
                speed = Mathf.Max(speed, 0.4f);
            }
            else speed = Mathf.Min(maxSpeed, speed + accel * dt);
            float step = Mathf.Min(speed * dt, d);
            headS += dir * step;
            if ((target - headS) * dir <= 0.02f) { headS = target; speed = 0f; return true; }
            return false;
        }

        void Depart(int toStop)
        {
            state = State.Running; stateTime = 0f; stopIndex = toStop; brakingCued = false; hopStart = 0f;
            Cue?.Invoke("depart");
            if (PlayerAboard && toStop < stops.Length)
                SubwayAnnouncer.Say($"The next station is {stops[toStop].stationName}. {stops[toStop].stationName} station.");
        }

        void Arrive()
        {
            if (hopStart >= 0f && stopIndex > 0)
            {
                LastHopSeconds = stateTime;
                LastHop = $"{stops[stopIndex - 1].stationName} -> {stops[stopIndex].stationName}";
            }
            hopStart = -1f;
            state = State.Dwell; stateTime = 0f; speed = 0f; closingSaid = false; sayAgain = 0f;
            Cue?.Invoke("arrive");
            if (PlayerAboard)
            {
                string n = stops[stopIndex].stationName;
                SubwayAnnouncer.Say(AtTerminal ? $"{n} station. This train is short turning. All customers must exit at {n}."
                                               : $"{n} station. Doors will open on the right side.");
            }
        }

        void DwellStep(float dt)
        {
            float closeAt = dwellSeconds - doorSeconds;
            bool holdForPlayer = AtTerminal && PlayerAboard;      // never carry anyone into the dead-end tunnel
            if (stateTime < doorSeconds)
            {
                if (doorOpen == 0f) Cue?.Invoke("doors_open");
                doorOpen = Mathf.Clamp01(stateTime / doorSeconds);
            }
            else if (holdForPlayer && stateTime >= closeAt - 1.5f)
            {
                doorOpen = 1f;
                stateTime = closeAt - 1.6f;                       // stay open
                sayAgain -= dt;
                if (sayAgain <= 0f)
                {
                    sayAgain = 10f;
                    SubwayAnnouncer.Say($"This train is short turning. All customers must exit at {stops[stopIndex].stationName}.");
                }
            }
            else if (stateTime < closeAt) doorOpen = 1f;
            else
            {
                if (!closingSaid)
                {
                    closingSaid = true;
                    Cue?.Invoke("chime");
                    if (PlayerAboard || PlayerOnMyPlatform(stops[stopIndex])) SubwayAnnouncer.Say("Doors closing, please stand clear of the doors.");
                }
                doorOpen = Mathf.Clamp01(1f - (stateTime - closeAt) / doorSeconds);
                if (stateTime >= dwellSeconds)
                {
                    doorOpen = 0f;
                    Cue?.Invoke("doors_close");
                    if (AtTerminal) { state = State.Leaving; stateTime = 0f; brakingCued = false; Cue?.Invoke("depart"); }
                    else Depart(stopIndex + 1);
                }
            }
        }

        // ------------------------------------------------------------------ cars and the rider
        void PlaceCars()
        {
            if (cars == null || line == null) return;
            foreach (var c in cars)
            {
                float s = headS - dir * c.offsetFromHead;
                c.transform.SetPositionAndRotation(line.TrackPoint(s, dir), line.TrackRotation(s, dir));
                c.SetDoors(doorOpen);
            }
            Physics.SyncTransforms();
        }

        int PlayerCar()
        {
            if (player == null) return -1;
            var p = player.transform.position + Vector3.up * 0.9f;
            for (int i = 0; i < cars.Length; i++) if (cars[i].Contains(p)) return i;
            return -1;
        }

        void Carry(int i)
        {
            var local = before[i].inverse.MultiplyPoint3x4(player.transform.position);
            var now = cars[i].transform.localToWorldMatrix.MultiplyPoint3x4(local);
            float dyaw = Mathf.DeltaAngle(beforeYaw[i], cars[i].transform.eulerAngles.y);
            if ((now - player.transform.position).sqrMagnitude < 1e-8f && Mathf.Abs(dyaw) < 1e-4f) return;
            if (playerCc != null) playerCc.enabled = false;
            player.transform.position = now;
            if (playerCc != null) playerCc.enabled = true;
            if (Mathf.Abs(dyaw) > 1e-4f) player.SetLook(player.Yaw + dyaw, player.Pitch);
        }

        // ------------------------------------------------------------------ quick travel
        bool PlayerOnMyPlatform(SubwayStation st) => player != null && st.OnPlatform(player.transform.position, out int d) && d == dir;

        // Tarik is waiting on one of this train's platforms: make sure the train is on its way to it
        void Dispatch()
        {
            if (player == null) return;
            var at = SubwayLine.Instance.StationAt(player.transform.position, out int pd);
            if (at == null || pd != dir) return;
            int idx = Array.IndexOf(stops, at);
            bool coming = (state == State.Running || state == State.Dwell) && stopIndex == idx;
            if (coming) return;
            bool behind = (state == State.Running || state == State.Dwell) && stopIndex < idx;
            float gap = Mathf.Abs(headS - at.s);
            if (behind && ETA(idx) <= 25f) return;
            if (state != State.Hidden && gap < FarEnough) return;     // still in sight: let it go first
            // send it in from the tunnel, already at cruising speed, out of sight behind the platform
            headS = StopHead(idx) - dir * StagingBack;
            speed = maxSpeed; doorOpen = 0f; state = State.Running; stopIndex = idx; stateTime = 0f; brakingCued = false; hopStart = -1f;
        }

        // rough seconds until the train stands at stop idx
        public float ETA(int idx)
        {
            if (state == State.Dwell && stopIndex == idx) return 0f;
            if (state == State.Running && stopIndex == idx)
                return Mathf.Abs(StopHead(idx) - headS) / Mathf.Max(8f, (speed + maxSpeed) * 0.5f) + 2f;
            if ((state == State.Running || state == State.Dwell) && stopIndex < idx)
                return Mathf.Abs(StopHead(idx) - headS) / 14f + (idx - stopIndex) * dwellSeconds;
            return 999f;
        }

        // tools (ShotTool, tests): stand at stop i with the doors at `open`
        public void PlaceAt(int i, float open)
        {
            if (!Ready) Init();
            state = State.Dwell; stopIndex = i; headS = StopHead(i); speed = 0f; doorOpen = open;
            stateTime = open > 0f ? doorSeconds + 0.1f : 0f;
            PlaceCars();
        }

        public bool CanZoneOut => PlayerAboard && !zoning && ((state == State.Running) || (state == State.Dwell && !AtTerminal && stateTime >= dwellSeconds - doorSeconds));

        // jump straight to the next stop (doors about to open); the rider comes along with the car
        public void ZoneOutNow()
        {
            if (!CanZoneOut) return;
            int next = state == State.Running ? stopIndex : stopIndex + 1;
            for (int i = 0; i < cars.Length; i++) { before[i] = cars[i].transform.localToWorldMatrix; beforeYaw[i] = cars[i].transform.eulerAngles.y; }
            int carIdx = PlayerCar();
            stopIndex = next; headS = StopHead(next); speed = 0f; doorOpen = 0f;
            Arrive();
            LastHop = "zoned out";
            PlaceCars();
            if (carIdx >= 0) Carry(carIdx);
        }

        IEnumerator ZoneOutFade()
        {
            zoning = true;
            var a = SubwayAnnouncer.Instance;
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime) { if (a) a.Fade = t / 0.6f; yield return null; }
            if (a) a.Fade = 1f;
            zoning = false;
            ZoneOutNow();
            yield return new WaitForSecondsRealtime(0.5f);
            for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime) { if (a) a.Fade = 1f - t / 0.6f; yield return null; }
            if (a) a.Fade = 0f;
        }

        void UpdateStatus()
        {
            var a = SubwayAnnouncer.Instance;
            if (a == null || player == null) return;
            if (PlayerAboard)
            {
                a.SetStatus(CanZoneOut ? $"[Enter]  Zone out to {stops[state == State.Running ? stopIndex : Mathf.Min(stopIndex + 1, stops.Length - 1)].stationName}" : "");
                return;
            }
            var at = SubwayLine.Instance.StationAt(player.transform.position, out int pd);
            if (at == null || pd != dir) return;
            int idx = Array.IndexOf(stops, at);
            float eta = ETA(idx);
            string when = state == State.Dwell && stopIndex == idx ? "boarding" : eta < 20f ? "arriving" : $"{Mathf.CeilToInt(eta / 60f)} min";
            a.SetStatus($"1  {Destination}  –  {when}");
        }
    }
}
