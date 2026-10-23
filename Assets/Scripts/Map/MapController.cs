using System;
using LifeInDowntown.Game;
using LifeInDowntown.Phone;
using LifeInDowntown.Player;
using MinniGameFramework.Player;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine;

namespace LifeInDowntown.Map
{
    // GTA-style navigation (docs/DESIGN.md 3F): a circular minimap bottom-left that turns with the camera,
    // and a full-screen map on M (pauses the game; drag to pan, scroll to zoom). Both draw the image made
    // by Tools/osm/render_map.py from the real OSM city data.
    [RequireComponent(typeof(UIDocument))]
    public class MapController : MonoBehaviour
    {
        [Serializable] class Meta { public float xmin, xmax, zmin, zmax, pxPerMetre; public int width, height; }

        public BodycamPlayer player;
        public BodycamRig rig;
        public MissionManager missions;

        [Header("Minimap")]
        public float minimapMetresAcross = 300f;   // ~150 m radius, like GTA's on-foot zoom
        const float MinimapSize = 300f;

        public static bool IsOpen { get; private set; }
        // Esc that closed the map must not also open the pause menu in the same frame
        public static bool ConsumedEscThisFrame => IsOpen || closedFrame == Time.frameCount;
        static int closedFrame = -1;

        VisualElement root, minimap, miniImage, miniBlip, north, full, fullView, fullImage, fullBlip, fullArrow;
        Label fullObjective;
        Meta meta;
        float fullScale = 0.35f;   // screen px per map px in the full map
        Vector2 fullOffset;        // where the map image's top-left sits in the view
        bool dragging;
        Vector2 dragStart, offsetStart;

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            minimap = root.Q("minimap");
            miniImage = root.Q("minimap-image");
            miniBlip = root.Q("minimap-blip");
            north = root.Q("minimap-north");
            full = root.Q("fullmap");
            fullView = root.Q("fullmap-view");
            fullImage = root.Q("fullmap-image");
            fullBlip = root.Q("fullmap-blip");
            fullArrow = root.Q("fullmap-arrow");
            fullObjective = root.Q<Label>("fullmap-objective");

            var tex = Resources.Load<Texture2D>("City/map");
            meta = JsonUtility.FromJson<Meta>(Resources.Load<TextAsset>("City/map_meta").text);
            miniImage.style.backgroundImage = new StyleBackground(tex);
            fullImage.style.backgroundImage = new StyleBackground(tex);
            float m = MinimapSize / minimapMetresAcross;   // minimap px per metre
            miniImage.style.width = meta.width / meta.pxPerMetre * m;
            miniImage.style.height = meta.height / meta.pxPerMetre * m;

            fullView.RegisterCallback<PointerDownEvent>(e => { dragging = true; dragStart = e.position; offsetStart = fullOffset; fullView.CapturePointer(e.pointerId); });
            fullView.RegisterCallback<PointerMoveEvent>(e => { if (dragging) { fullOffset = offsetStart + ((Vector2)e.position - dragStart); LayoutFull(); } });
            fullView.RegisterCallback<PointerUpEvent>(e => { dragging = false; fullView.ReleasePointer(e.pointerId); });
            fullView.RegisterCallback<WheelEvent>(e => Zoom(e.delta.y < 0 ? 1.15f : 1f / 1.15f, e.mousePosition));
        }

        // world metres (x east, z north) -> map image pixels (u right, v down)
        Vector2 ToMap(Vector3 w) => new Vector2((w.x - meta.xmin) * meta.pxPerMetre, (meta.zmax - w.z) * meta.pxPerMetre);

        void Update()
        {
            var flow = GameFlow.Instance;
            bool playing = flow == null || flow.State == GameFlow.FlowState.Playing;
            if (!playing && IsOpen) SetOpen(false);
            root.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!playing || player == null) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.mKey.wasPressedThisFrame) SetOpen(!IsOpen);
                else if (IsOpen && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
                if (IsOpen && kb.spaceKey.wasPressedThisFrame) CentreOnPlayer();
            }
            if (IsOpen) UpdateFull(); else UpdateMini();
        }

        // ------------------------------------------------------------------ minimap
        void UpdateMini()
        {
            float m = MinimapSize / minimapMetresAcross;
            float yaw = rig != null ? rig.CamYaw : player.Yaw;
            Vector3 pos = player.transform.position;
            Vector2 pMap = ToMap(pos) / meta.pxPerMetre * m;          // player in minimap-image px
            miniImage.style.left = MinimapSize * 0.5f - pMap.x;
            miniImage.style.top = MinimapSize * 0.5f - pMap.y;
            miniImage.style.transformOrigin = new TransformOrigin(pMap.x, pMap.y);
            miniImage.style.rotate = new Rotate(-yaw);                 // heading up

            float rad = yaw * Mathf.Deg2Rad;
            Vector2 fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)), right = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));
            Vector2 OnDisc(Vector2 d, float clampR)   // world delta (x, z) -> minimap px from centre, clamped to the rim
            {
                var s = new Vector2(Vector2.Dot(d, right), -Vector2.Dot(d, fwd)) * m;
                return s.magnitude > clampR ? s.normalized * clampR : s;
            }
            var n = OnDisc(new Vector2(0f, 1000f), MinimapSize * 0.5f - 4f);
            north.style.left = MinimapSize * 0.5f + n.x - 15f;
            north.style.top = MinimapSize * 0.5f + n.y - 15f;

            var target = missions != null ? missions.Target : null;
            miniBlip.style.display = target.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (target.HasValue)
            {
                var b = OnDisc(new Vector2(target.Value.x - pos.x, target.Value.z - pos.z), MinimapSize * 0.5f - 14f);
                miniBlip.style.left = MinimapSize * 0.5f + b.x - 9f;
                miniBlip.style.top = MinimapSize * 0.5f + b.y - 9f;
            }
        }

        // ---------------------------------------------------------------- full map
        void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            if (!open) closedFrame = Time.frameCount;
            full.EnableInClassList("fullmap--open", open);
            minimap.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
            // only hand control back if we're still playing (not if a menu took over)
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (open || playing)
            {
                Time.timeScale = open ? 0f : 1f;
                if (player != null) player.enabled = !open;
                if (rig != null) rig.enabled = !open;
                UnityEngine.Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
                UnityEngine.Cursor.visible = open;
            }
            if (open) { fullScale = Mathf.Clamp(fullScale, 0.15f, 2f); CentreOnPlayer(); }
        }

        void CentreOnPlayer()
        {
            var view = fullView.layout;
            float vw = float.IsNaN(view.width) || view.width < 1f ? 1920f : view.width;
            float vh = float.IsNaN(view.height) || view.height < 1f ? 1080f : view.height;
            fullOffset = new Vector2(vw * 0.5f, vh * 0.5f) - ToMap(player.transform.position) * fullScale;
            LayoutFull();
        }

        void Zoom(float factor, Vector2 around)
        {
            float next = Mathf.Clamp(fullScale * factor, 0.15f, 2f);
            Vector2 mapPt = (around - fullOffset) / fullScale;   // keep the point under the cursor fixed
            fullScale = next;
            fullOffset = around - mapPt * fullScale;
            LayoutFull();
        }

        void LayoutFull()
        {
            fullImage.style.left = fullOffset.x;
            fullImage.style.top = fullOffset.y;
            fullImage.style.width = meta.width * fullScale;
            fullImage.style.height = meta.height * fullScale;
        }

        void UpdateFull()
        {
            Vector2 p = ToMap(player.transform.position) * fullScale;
            fullArrow.style.left = p.x - 17f;
            fullArrow.style.top = p.y - 17f;
            fullArrow.style.rotate = new Rotate(rig != null ? rig.CamYaw : player.Yaw);
            var target = missions != null ? missions.Target : null;
            fullBlip.style.display = target.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (target.HasValue)
            {
                Vector2 t = ToMap(target.Value) * fullScale;
                fullBlip.style.left = t.x - 13f;
                fullBlip.style.top = t.y - 13f;
            }
            var obj = missions?.ActiveObjective;
            fullObjective.text = obj != null ? $"{missions.Active.title}: {DialogueRunner.Fill(obj.text)}" : "No active job";
        }

        // verification hook for capture scripts: -openmap
        System.Collections.IEnumerator Start()
        {
            yield return new WaitForSeconds(2f);
            if (GameFlow.HasArg("-openmap")) SetOpen(true);
        }
    }
}
