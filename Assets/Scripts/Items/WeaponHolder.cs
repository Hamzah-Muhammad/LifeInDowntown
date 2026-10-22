using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Items
{
    // The equipped weapon (docs/DESIGN.md 3M), carry-and-draw only: Q draws or holsters it, R reloads
    // from loose rounds. Drawn, it comes up into the bottom of the chest-cam frame, the way a bodycam
    // sees a pistol held out in both hands. No firing yet: combat is its own milestone.
    // The pistol is Rocketbox Police_Male_01's duty pistol (EditorTools/PistolImport); the box stand-in is the fallback.
    public class WeaponHolder : MonoBehaviour
    {
        public static WeaponHolder Instance { get; private set; }

        public GameObject gunModel;                         // optional real model; otherwise the stand-in
        // camera space: out in front of the chest, a little right, canted and yawed in so the slide's side
        // shows (a straight-on pistol reads as a black bar from a chest mount)
        public Vector3 heldPosition = new Vector3(0.08f, -0.12f, 0.5f);
        public Vector3 heldEuler = new Vector3(-6f, -16f, 8f);
        public float drawSeconds = 0.35f;

        Transform gun;
        float raise;                                        // 0 holstered, 1 up

        static GameState S => GameState.Current;

        void Awake() { Instance = this; }

        void Start()
        {
            gun = (gunModel != null ? Instantiate(gunModel) : StandIn()).transform;
            gun.SetParent(transform, false);
            gun.gameObject.SetActive(false);
        }

        void Update()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            var kb = Keyboard.current;
            if (playing && kb != null && !InventoryUI.IsOpen && !LifeInDowntown.Map.MapController.IsOpen)
            {
                if (kb.qKey.wasPressedThisFrame) ToggleDraw();
                if (kb.rKey.wasPressedThisFrame && S.drawn)
                {
                    int got = Inventory.Reload(S);
                    LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(got > 0 ? "RELOADED" : "NO AMMO", got > 0 ? $"{S.loaded} in the mag." : "No spare rounds.");
                }
            }
            if (!string.IsNullOrEmpty(S.equipped) && Inventory.Count(S, S.equipped) == 0) { S.equipped = ""; S.drawn = false; }
            raise = Mathf.MoveTowards(raise, S.drawn ? 1f : 0f, Time.deltaTime / drawSeconds);
            if (gun == null) return;
            gun.gameObject.SetActive(raise > 0.01f);
            float e = raise * raise * (3f - 2f * raise);    // smoothstep: out of the holster, up, settle
            gun.localPosition = Vector3.Lerp(heldPosition + new Vector3(0.12f, -0.3f, -0.15f), heldPosition, e);
            gun.localRotation = Quaternion.Euler(Vector3.Lerp(heldEuler + new Vector3(50f, 20f, 0f), heldEuler, e));
        }

        public void ToggleDraw()
        {
            if (string.IsNullOrEmpty(S.equipped)) { LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner("NOTHING EQUIPPED", "Equip a weapon in the backpack (Tab)."); return; }
            S.drawn = !S.drawn;
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
