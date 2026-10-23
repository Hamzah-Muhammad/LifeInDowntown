using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.NPC;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Items
{
    // The equipped weapon (docs/DESIGN.md 3M, 3O.6). Q draws or holsters, right mouse aims down the sights
    // (the lens narrows a little), left mouse fires, R reloads from loose rounds. Each shot: a raycast down the
    // line of sight (a little spread from the hip), recoil on the gun and the chest cam, a muzzle flash and light,
    // a gunshot with the street answering back, a casing, and a bullet hole or a person going down. Everyone
    // within earshot runs, police walk over and watch Tarik, and HEAT goes up. Not indoors: "Not here."
    // The pistol is Rocketbox Police_Male_01's duty pistol (EditorTools/PistolImport); the box stand-in is the fallback.
    public class WeaponHolder : MonoBehaviour
    {
        public static WeaponHolder Instance { get; private set; }

        public GameObject gunModel;
        // camera space: out in front of the chest, a little right, canted and yawed in so the slide's side
        // shows (a straight-on pistol reads as a black bar from a chest mount)
        public Vector3 heldPosition = new Vector3(0.10f, -0.15f, 0.42f);  // ready, low right of the frame, hands in view
        public Vector3 heldEuler = new Vector3(6f, -6f, 4f);
        public Vector3 aimPosition = new Vector3(0.0f, -0.065f, 0.45f);   // sights on the eye line, 45 cm out (the stance studio)
        public Vector3 aimEuler = new Vector3(0f, 0f, 0f);
        public float drawSeconds = 0.35f, aimFov = 52f, fireInterval = 0.16f, range = 140f;
        public float hipSpread = 2.2f, aimSpread = 0.35f, recoilDegrees = 2.4f;
        public float panicRadius = 35f;
        public AudioClip shot, dry, casing;

        Transform gun, muzzle;
        // hands (DESIGN 3P.1): grip points on the gun for Tarik's IK arms, elbow hints, the moving reload hand
        Transform gripR, gripL, hintR, hintL, reloadHand;
        TarikBody body;
        float reloadT = -1f; bool reloadEmpty, magOut, magIn;
        GameObject handMag;
        public const float ReloadSeconds = 1.6f;
        // the reload: gun pulled in to the middle of the view, muzzle up, canted so the mag well faces the left hand
        public static readonly Vector3 ReloadPos = new Vector3(0.03f, -0.12f, 0.36f), ReloadEuler = new Vector3(-20f, 8f, 30f);
        // grip points on the pistol (wrist positions, hand frame: fingers along forward, palm to -X right / +X left)
        // and elbow hints in chest-cam space: elbows down and a little out (shared with EditorTools/StanceStudio)
        // right: wrist just behind the backstrap, the hand carrying on the forearm's line (web high on the grip,
        // fingers round the front strap); left: wrist low on the left, wrapping over the right hand's fingers
        public static readonly Vector3 GripRPos = new Vector3(0.022f, -0.03f, -0.15f), GripLPos = new Vector3(-0.06f, -0.045f, -0.105f);
        public static readonly Quaternion GripRRot = Quaternion.Euler(25f, -6f, 0f), GripLRot = Quaternion.Euler(22f, 40f, 0f);   // support fingers across the front strap, over the right hand's
        public static readonly Vector3 HintR = new Vector3(0.35f, -0.9f, 0.1f), HintL = new Vector3(-0.35f, -0.9f, 0.1f);   // eye-camera space: elbows down, a little out
        Light flash;
        AudioSource audioSrc;
        Camera cam;
        BodycamRig rig;
        float raise, aim, baseFov, nextShot, flashUntil, gunKick;
        string toast; float toastUntil;
        readonly Queue<GameObject> holes = new Queue<GameObject>();
        readonly Queue<GameObject> casings = new Queue<GameObject>();
        static Material holeMat, brassMat, magMat;

        static GameState S => GameState.Current;
        public bool Aiming => aim > 0.5f;

        void Awake() { Instance = this; }

        // playing, on foot, no backpack / map / phone up: the hands are his (the gun and the fists both ask)
        public static bool InputFree()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            return playing && !LifeInDowntown.Vehicles.PlayerDriving.PlayerIsDriving && !InventoryUI.IsOpen && !LifeInDowntown.Map.MapController.IsOpen
                   && !(LifeInDowntown.Phone.PhoneController.Instance != null && LifeInDowntown.Phone.PhoneController.Instance.IsUp);
        }

        // Tarik's own body under the player (NPC fighters carry a TarikBody for their swings too)
        public static TarikBody PlayerBody(Component cam)
        {
            var r = cam.GetComponent<BodycamRig>();
            return r != null && r.player != null ? r.player.GetComponentInChildren<TarikBody>() : null;
        }

        void Start()
        {
            gun = (gunModel != null ? Instantiate(gunModel) : StandIn()).transform;
            gun.SetParent(transform, false);
            gun.gameObject.SetActive(false);
            // a gun 45 cm from the lens throws a coarse, blotchy shadow over his sleeves: it casts none (as the stand-in)
            foreach (var r in gun.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = Bounds(gun);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, b.max.y - 0.018f, b.max.z + 0.01f);
            gripR = Point(gun, "GripR", GripRPos, GripRRot);
            gripL = Point(gun, "GripL", GripLPos, GripLRot);
            hintR = Point(transform, "ElbowHintR", HintR, Quaternion.identity);
            hintL = Point(transform, "ElbowHintL", HintL, Quaternion.identity);
            reloadHand = Point(transform, "ReloadHand", Vector3.zero, Quaternion.identity);
            body = PlayerBody(this);
            flash = muzzle.gameObject.AddComponent<Light>();
            flash.type = LightType.Point; flash.range = 8f; flash.color = new Color(1f, 0.78f, 0.45f); flash.intensity = 0f;
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0f;
            if (shot == null) shot = Resources.Load<AudioClip>("Audio/gunshot_9mm");
            if (dry == null) dry = Resources.Load<AudioClip>("Audio/gun_dry");
            if (casing == null) casing = Resources.Load<AudioClip>("Audio/casing_tink");
            cam = GetComponent<Camera>();
            rig = GetComponent<BodycamRig>();
            baseFov = cam != null ? cam.fieldOfView : 68f;
        }

        void Update()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            bool driving = LifeInDowntown.Vehicles.PlayerDriving.PlayerIsDriving;
            var kb = Keyboard.current; var mouse = Mouse.current;
            bool free = InputFree();
            if (free && kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) ToggleDraw();
                if (kb.rKey.wasPressedThisFrame && S.drawn) StartReload();
            }
            if (driving && S.drawn) S.drawn = false;
            bool wantAim = free && S.drawn && mouse != null && mouse.rightButton.isPressed;
            if (free && S.drawn && mouse != null && mouse.leftButton.wasPressedThisFrame && raise > 0.95f && cam != null && reloadT < 0f)
            {
                float spread = Mathf.Lerp(hipSpread, aimSpread, aim);
                var dir = Quaternion.Euler(Random.Range(-spread, spread) * 0.5f, Random.Range(-spread, spread) * 0.5f, 0f) * cam.transform.forward;
                Fire(cam.transform.position, dir);
            }
            if (!string.IsNullOrEmpty(S.equipped) && Inventory.Count(S, S.equipped) == 0) { S.equipped = ""; S.drawn = false; }

            raise = Mathf.MoveTowards(raise, S.drawn ? 1f : 0f, Time.deltaTime / drawSeconds);
            aim = Mathf.MoveTowards(aim, wantAim ? 1f : 0f, Time.deltaTime * 5f);
            gunKick = Mathf.MoveTowards(gunKick, 0f, Time.deltaTime * 6f);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(baseFov, aimFov, aim * raise);
            if (flash != null) flash.intensity = Time.time < flashUntil ? 12000f : 0f;
            if (gun == null) return;
            gun.gameObject.SetActive(raise > 0.01f);
            float e = raise * raise * (3f - 2f * raise);    // smoothstep: out of the holster, up, settle
            Vector3 held = Vector3.Lerp(heldPosition, aimPosition, aim) + new Vector3(0f, gunKick * 0.02f, -gunKick * 0.05f);
            Vector3 heldRot = Vector3.Lerp(heldEuler, aimEuler, aim) + new Vector3(-gunKick * 9f, 0f, 0f);
            // reloading: the gun tips in and down while the left hand works (DESIGN 3P.1)
            float tilt = 0f;
            if (reloadT >= 0f)
            {
                reloadT += Time.deltaTime;
                float r = reloadT;
                tilt = r < 0.3f ? r / 0.3f : r > 1.4f ? Mathf.Clamp01((ReloadSeconds - r) / 0.2f) : 1f;
                StepReload(r);
                if (r >= ReloadSeconds || !S.drawn) EndReload();
            }
            held = Vector3.Lerp(held, ReloadPos, tilt);
            heldRot = Vector3.Lerp(heldRot, ReloadEuler, tilt);
            gun.localPosition = Vector3.Lerp(heldPosition + new Vector3(0.12f, -0.3f, -0.15f), held, e);
            gun.localRotation = Quaternion.Euler(Vector3.Lerp(heldEuler + new Vector3(50f, 20f, 0f), heldRot, e));
            Hands(e);
        }

        // ---------------------------------------------------------------- hands and reload
        void Hands(float e)
        {
            if (body == null) return;
            body.rightTarget = gripR; body.rightHint = hintR; body.rightWeight = raise > 0.01f ? Mathf.Clamp01(raise * 1.4f) : 0f;
            body.leftTarget = reloadT >= 0f ? reloadHand : gripL; body.leftHint = hintL;
            body.leftWeight = raise <= 0.01f ? 0f : reloadT >= 0f ? 1f : Mathf.Clamp01((e - 0.6f) / 0.4f);   // the support hand joins once the gun is up; holstered, both hands are free
        }

        public bool Reloading => reloadT >= 0f;

        public bool StartReload()
        {
            var d = ItemCatalog.Get(S.equipped);
            if (d == null || reloadT >= 0f || !S.drawn) return false;
            if (S.loaded >= d.magazine) { Toast("Full."); return false; }
            if (Inventory.Count(S, d.ammo) == 0) { Toast("No spare rounds."); return false; }
            reloadT = 0f; reloadEmpty = S.loaded == 0; magOut = magIn = false;
            return true;
        }

        // the left hand's path: grip -> belt (the old mag falls) -> new mag to the well -> (rack if it was empty) -> grip
        public void StepReload(float r)
        {
            Vector3 belt = transform.TransformPoint(new Vector3(-0.14f, -0.62f, 0.08f));
            Quaternion beltRot = transform.rotation * Quaternion.Euler(70f, 10f, 0f);
            Vector3 well = gun.TransformPoint(new Vector3(0f, -0.11f, -0.085f));
            Quaternion wellRot = gun.rotation * Quaternion.Euler(80f, 0f, 0f);
            // overhand on the rear of the slide: wrist on the left, fingers across the top to the right, palm down
            Vector3 slide = gun.TransformPoint(new Vector3(-0.06f, 0.06f, -0.06f));
            Quaternion slideRot = gun.rotation * Quaternion.LookRotation(Vector3.right, Vector3.back);
            Vector3 p; Quaternion q;
            float k(float a, float b) { float t = Mathf.Clamp01((r - a) / (b - a)); return t * t * (3f - 2f * t); }
            if (r < 0.3f) { p = gripL.position; q = gripL.rotation; }
            else if (r < 0.7f) { p = Vector3.Lerp(gripL.position, belt, k(0.3f, 0.7f)); q = Quaternion.Slerp(gripL.rotation, beltRot, k(0.3f, 0.7f)); }
            else if (r < 1.15f) { p = Vector3.Lerp(belt, well, k(0.7f, 1.1f)); q = Quaternion.Slerp(beltRot, wellRot, k(0.7f, 1.1f)); }
            else if (reloadEmpty && r < 1.4f)
            {
                float pull = k(1.22f, 1.32f) - k(1.32f, 1.4f);
                p = Vector3.Lerp(well, slide, k(1.15f, 1.22f)) - gun.forward * 0.04f * pull; q = slideRot;
            }
            else
            {
                var from = reloadEmpty ? slide : well; var fromRot = reloadEmpty ? slideRot : wellRot;
                float t0 = reloadEmpty ? 1.4f : 1.15f;
                p = Vector3.Lerp(from, gripL.position, k(t0, ReloadSeconds)); q = Quaternion.Slerp(fromRot, gripL.rotation, k(t0, ReloadSeconds));
            }
            reloadHand.SetPositionAndRotation(p, q);

            if (!magOut && r >= 0.3f) { magOut = true; DropMag(); }
            if (handMag == null && r >= 0.7f && r < 1.15f) handMag = Mag(false);
            if (handMag != null) handMag.transform.SetPositionAndRotation(p + q * new Vector3(0f, -0.02f, 0.06f), gun.rotation);
            if (!magIn && r >= 1.15f)
            {
                magIn = true;
                if (handMag != null) { Kill(handMag); handMag = null; }
                S.loaded = 0;                                     // the old mag fell out with whatever was left in it
                Inventory.Reload(S);
            }
        }

        void EndReload()
        {
            if (!magIn && magOut) { S.loaded = 0; Inventory.Reload(S); }   // cut short after the mag dropped: still loads
            if (handMag != null) { Kill(handMag); handMag = null; }
            reloadT = -1f;
            Toast($"{S.loaded} in the mag");
        }

        public int DroppedMags { get; private set; }

        void DropMag()
        {
            DroppedMags++;
            var m = Mag(true);
            m.transform.SetPositionAndRotation(gun.TransformPoint(new Vector3(0f, -0.09f, -0.085f)), gun.rotation);
            if (!Application.isPlaying) { Kill(m); return; }
            var rb = m.AddComponent<Rigidbody>(); rb.mass = 0.1f; rb.linearVelocity = -gun.up * 1.2f;
            Destroy(m, 20f);
        }

        GameObject Mag(bool physical)
        {
            if (magMat == null) { magMat = new Material(Shader.Find("HDRP/Lit")); magMat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f)); magMat.SetFloat("_Smoothness", 0.4f); }
            var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m.name = physical ? "DroppedMag" : "HandMag";
            m.transform.localScale = new Vector3(0.022f, 0.11f, 0.032f);
            m.GetComponent<Renderer>().sharedMaterial = magMat;
            if (!physical) Kill(m.GetComponent<Collider>());
            return m;
        }

        static void Kill(Object o) { if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }

        static Transform Point(Transform parent, string name, Vector3 lp, Quaternion lr)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = lp; t.localRotation = lr;
            return t;
        }

        // editor renders and tests: the reload frozen at r seconds, hands and all
        public void PoseReloadAt(float r)
        {
            PoseForShot(false);
            reloadT = r; reloadEmpty = true; magOut = true;
            float tilt = r < 0.3f ? r / 0.3f : r > 1.4f ? Mathf.Clamp01((ReloadSeconds - r) / 0.2f) : 1f;
            gun.localPosition = Vector3.Lerp(heldPosition, ReloadPos, tilt);
            gun.localRotation = Quaternion.Euler(Vector3.Lerp(heldEuler, ReloadEuler, tilt));
            StepReload(r);
            Hands(1f);
            if (body != null) { body.Solve(); body.Solve(); }
        }

        // editor renders and tests: build the gun and grips now and pose it held or aimed, with the hands on it
        public void PoseForShot(bool aimed)
        {
            if (gun == null) Start();
            raise = 1f; aim = aimed ? 1f : 0f;
            gun.gameObject.SetActive(true);
            gun.localPosition = aimed ? aimPosition : heldPosition;
            gun.localRotation = Quaternion.Euler(aimed ? aimEuler : heldEuler);
            Hands(1f);
            if (body != null) { body.Solve(); body.Solve(); }
        }

        // editor renders: the gun back in the holster (the fists shots)
        public void PoseHolstered()
        {
            if (gun == null) Start();
            raise = 0f; aim = 0f; S.drawn = false; reloadT = -1f;
            gun.gameObject.SetActive(false);
            Hands(0f);
        }

        public void ToggleDraw()
        {
            if (string.IsNullOrEmpty(S.equipped)) { Toast("Nothing equipped. Tab to open the backpack."); return; }
            S.drawn = !S.drawn;
        }

        public enum Shot { Fired, Hit, Wounded, Killed, Empty, NotHere, NotReady }

        // one shot from origin along dir (the game and ShootingTest both call this)
        public Shot Fire(Vector3 origin, Vector3 dir)
        {
            if (Time.time < nextShot && Application.isPlaying) return Shot.NotReady;
            nextShot = Time.time + fireInterval;
            if (LifeInDowntown.World.IndoorZone.IsIndoors(origin)) { Toast("Not here."); return Shot.NotHere; }
            if (S.loaded <= 0) { Play(dry, 0.8f); Toast("Empty. R to reload."); return Shot.Empty; }
            S.loaded--;
            Play(shot, 1f);
            flashUntil = Time.time + 0.05f;
            gunKick = 1f;
            if (rig != null) rig.Kick(recoilDegrees * Mathf.Lerp(1f, 0.7f, aim));
            EjectCasing();

            int near = Panic(origin);
            if (near > 0) S.heat += 1;
            Hostile.HearShot(origin);   // anyone armed within 60 m comes looking (DESIGN 3P.4)
            var result = Shot.Fired;
            if (Physics.Raycast(origin + dir * 0.35f, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                var ped = hit.collider.GetComponentInParent<Pedestrian>();
                if (ped != null && !ped.Dead)   // DESIGN 3P.2: head = down at once, two body shots
                {
                    bool killed = ped.Damage(Pedestrian.BodyShot(), hit.point, dir * 40f, origin);
                    S.heat += !killed ? 2 : ped.role == Pedestrian.Role.Police ? 10 : 3;
                    result = killed ? Shot.Killed : Shot.Wounded;
                }
                else { Hole(hit); result = Shot.Hit; }
            }
            return result;
        }

        // a shot that isn't Tarik's (a hostile firing): the street reacts the same way
        public static void PanicAt(Vector3 at) { if (Instance != null) Instance.Panic(at); }

        // everyone within earshot runs from the shot; police walk over and watch
        int Panic(Vector3 at)
        {
            int n = 0;
            var all = new List<Pedestrian>(Pedestrian.All);
            foreach (var p in all)
            {
                if (p.Dead || p.role == Pedestrian.Role.Story || p.role == Pedestrian.Role.Hostile) continue;
                if ((p.transform.position - at).sqrMagnitude > panicRadius * panicRadius) continue;
                n++;
                if (p.role == Pedestrian.Role.Police) p.Approach(at); else p.Flee(at);
            }
            return n;
        }

        void Hole(RaycastHit hit)
        {
            if (holeMat == null)
            {
                var sh = Shader.Find("HDRP/Lit");
                holeMat = new Material(sh); holeMat.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.03f)); holeMat.SetFloat("_Smoothness", 0.1f);
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "BulletHole";
            Destroy(q.GetComponent<Collider>());
            q.GetComponent<Renderer>().sharedMaterial = holeMat;
            q.transform.SetPositionAndRotation(hit.point + hit.normal * 0.004f, Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            q.transform.localScale = Vector3.one * Random.Range(0.025f, 0.04f);
            q.transform.SetParent(hit.collider.transform, true);
            holes.Enqueue(q);
            while (holes.Count > 40) { var old = holes.Dequeue(); if (old != null) Destroy(old); }
        }

        void EjectCasing()
        {
            if (!Application.isPlaying || muzzle == null) return;
            if (brassMat == null)
            {
                brassMat = new Material(Shader.Find("HDRP/Lit"));
                brassMat.SetColor("_BaseColor", new Color(0.72f, 0.55f, 0.2f)); brassMat.SetFloat("_Metallic", 1f); brassMat.SetFloat("_Smoothness", 0.7f);
            }
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.name = "Casing";
            c.transform.localScale = new Vector3(0.01f, 0.0095f, 0.01f);
            c.transform.position = muzzle.position - muzzle.forward * 0.12f + muzzle.right * 0.02f;
            c.GetComponent<Renderer>().sharedMaterial = brassMat;
            Destroy(c.GetComponent<Collider>());
            var col = c.AddComponent<CapsuleCollider>(); col.radius = 0.5f; col.height = 2f;
            var rb = c.AddComponent<Rigidbody>(); rb.mass = 0.008f;
            rb.linearVelocity = (muzzle.right * 2.2f + muzzle.up * 1.6f) + Random.insideUnitSphere * 0.3f;
            rb.angularVelocity = Random.insideUnitSphere * 30f;
            casings.Enqueue(c);
            while (casings.Count > 20) { var old = casings.Dequeue(); if (old != null) Destroy(old); }
            if (casing != null) AudioSource.PlayClipAtPoint(casing, c.transform.position, 0.3f);
        }

        void Play(AudioClip clip, float vol) { if (clip != null && audioSrc != null) audioSrc.PlayOneShot(clip, vol); }

        void Toast(string t) { toast = t; toastUntil = Time.time + 2f; }

        GUIStyle hud;

        // ammo in the corner while the gun is out; short notes ("Not here.", "Reloaded")
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = Screen.height / 1080f;
            if (hud == null) hud = new GUIStyle { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.93f, 0.93f, 0.9f, 0.85f) } };
            hud.fontSize = Mathf.RoundToInt(30f * k);
            var d = ItemCatalog.Get(S.equipped);
            if (S.drawn && d != null)
                GUI.Label(new Rect(Screen.width - 330f * k, Screen.height - 80f * k, 300f * k, 50f * k), $"{S.loaded} / {Inventory.Count(S, d.ammo)}", hud);
            if (Time.time < toastUntil && toast != null)
                GUI.Label(new Rect(Screen.width - 630f * k, Screen.height - 130f * k, 600f * k, 40f * k), toast, hud);
        }

        public static Bounds Bounds(Transform t)   // the gun model's local bounds (Hostile finds its muzzle with it too)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            var mf = rs[0].GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null && rs.Length == 1 ? mf.sharedMesh.bounds : new Bounds(new Vector3(0, 0.01f, 0.02f), new Vector3(0.03f, 0.13f, 0.19f));
        }

        // compact black polymer pistol from boxes, ~18 cm long; the muzzle points along +Z
        public static GameObject StandIn()
        {
            var root = new GameObject("Pistol_StandIn");
            var shader = Shader.Find("HDRP/Lit");
            Material Mat(Color c, float smooth)   // in the editor these become scene-embedded materials
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0.2f);
                return m;
            }
            var slide = Mat(new Color(0.07f, 0.07f, 0.075f), 0.55f);
            var frame = Mat(new Color(0.045f, 0.045f, 0.05f), 0.3f);
            void Part(string name, Material m, Vector3 pos, Vector3 size, float tilt = 0f)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                p.name = name;
                if (Application.isPlaying) Destroy(p.GetComponent<Collider>()); else DestroyImmediate(p.GetComponent<Collider>());
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = pos; p.transform.localScale = size;
                p.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
                p.GetComponent<Renderer>().sharedMaterial = m;
                p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Part("Slide", slide, new Vector3(0f, 0.012f, 0.02f), new Vector3(0.026f, 0.03f, 0.18f));
            Part("Frame", frame, new Vector3(0f, -0.01f, 0.03f), new Vector3(0.024f, 0.02f, 0.15f));
            Part("Grip", frame, new Vector3(0f, -0.06f, -0.045f), new Vector3(0.027f, 0.1f, 0.045f), -16f);
            Part("TriggerGuard", frame, new Vector3(0f, -0.032f, 0.012f), new Vector3(0.01f, 0.006f, 0.05f));
            Part("SightFront", slide, new Vector3(0f, 0.03f, 0.105f), new Vector3(0.004f, 0.006f, 0.006f));
            Part("SightRear", slide, new Vector3(0f, 0.03f, -0.06f), new Vector3(0.018f, 0.006f, 0.006f));
            return root;
        }
    }
}
