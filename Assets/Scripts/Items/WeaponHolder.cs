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
        public Vector3 heldPosition = new Vector3(0.08f, -0.12f, 0.5f);
        public Vector3 heldEuler = new Vector3(-6f, -16f, 8f);
        public Vector3 aimPosition = new Vector3(0.0f, -0.085f, 0.45f);   // up on the line of sight
        public Vector3 aimEuler = new Vector3(0f, 0f, 0f);
        public float drawSeconds = 0.35f, aimFov = 52f, fireInterval = 0.16f, range = 140f;
        public float hipSpread = 2.2f, aimSpread = 0.35f, recoilDegrees = 2.4f;
        public float panicRadius = 35f;
        public AudioClip shot, dry, casing;

        Transform gun, muzzle;
        Light flash;
        AudioSource audioSrc;
        Camera cam;
        BodycamRig rig;
        float raise, aim, baseFov, nextShot, flashUntil, gunKick;
        string toast; float toastUntil;
        readonly Queue<GameObject> holes = new Queue<GameObject>();
        readonly Queue<GameObject> casings = new Queue<GameObject>();
        static Material holeMat, brassMat;

        static GameState S => GameState.Current;
        public bool Aiming => aim > 0.5f;

        void Awake() { Instance = this; }

        void Start()
        {
            gun = (gunModel != null ? Instantiate(gunModel) : StandIn()).transform;
            gun.SetParent(transform, false);
            gun.gameObject.SetActive(false);
            var b = Bounds(gun);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, b.max.y - 0.018f, b.max.z + 0.01f);
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
            bool free = playing && !driving && !InventoryUI.IsOpen && !LifeInDowntown.Map.MapController.IsOpen
                        && !(LifeInDowntown.Phone.PhoneController.Instance != null && LifeInDowntown.Phone.PhoneController.Instance.IsUp);
            if (free && kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) ToggleDraw();
                if (kb.rKey.wasPressedThisFrame && S.drawn) Toast(Inventory.Reload(S) > 0 ? $"Reloaded  {S.loaded}" : "No spare rounds.");
            }
            if (driving && S.drawn) S.drawn = false;
            bool wantAim = free && S.drawn && mouse != null && mouse.rightButton.isPressed;
            if (free && S.drawn && mouse != null && mouse.leftButton.wasPressedThisFrame && raise > 0.95f && cam != null)
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
            gun.localPosition = Vector3.Lerp(heldPosition + new Vector3(0.12f, -0.3f, -0.15f), held, e);
            gun.localRotation = Quaternion.Euler(Vector3.Lerp(heldEuler + new Vector3(50f, 20f, 0f), heldRot, e));
        }

        public void ToggleDraw()
        {
            if (string.IsNullOrEmpty(S.equipped)) { Toast("Nothing equipped. Tab to open the backpack."); return; }
            S.drawn = !S.drawn;
        }

        public enum Shot { Fired, Hit, Killed, Empty, NotHere, NotReady }

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
            var result = Shot.Fired;
            if (Physics.Raycast(origin + dir * 0.35f, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            {
                var ped = hit.collider.GetComponentInParent<Pedestrian>();
                if (ped != null && !ped.Dead)
                {
                    ped.Kill(hit.point, dir * 40f);
                    S.heat += 3;
                    result = Shot.Killed;
                }
                else { Hole(hit); result = Shot.Hit; }
            }
            return result;
        }

        // everyone within earshot runs from the shot; police walk over and watch
        int Panic(Vector3 at)
        {
            int n = 0;
            var all = new List<Pedestrian>(Pedestrian.All);
            foreach (var p in all)
            {
                if (p.Dead || p.role == Pedestrian.Role.Story) continue;
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

        static Bounds Bounds(Transform t)
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
