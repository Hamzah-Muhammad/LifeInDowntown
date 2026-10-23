using System;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Game
{
    // Main menu / playing / paused (docs/DESIGN.md 3C). The game opens on the main menu over a slow
    // camera drift down Yonge; Esc pauses. Quitting from the pause menu writes the autosave.
    public class GameFlow : MonoBehaviour
    {
        public enum FlowState { MainMenu, Playing, Paused }

        public static GameFlow Instance { get; private set; }
        public FlowState State { get; private set; } = FlowState.MainMenu;

        public BodycamPlayer player;
        public BodycamRig rig;
        public MenuCamera menuCamera;
        public MenuController menu;

        Vector3 spawnPosition;
        float spawnYaw, playSeconds;

        void Awake()
        {
            Instance = this;
            spawnPosition = player.transform.position;
            spawnYaw = player.transform.eulerAngles.y;
            GameSettings.Load();
        }

        void Start()
        {
            GameSettings.Apply();
            EnterMainMenu();
            // verification hook: skip the menu straight into a new game (capture scripts)
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-autostart") >= 0) NewGame();
            if (HasArg("-lookatnpc")) LookAtNearestNpc();
        }

        // verification hook: stand ~5 m from the nearest pedestrian, facing them
        void LookAtNearestNpc()
        {
            LifeInDowntown.NPC.Pedestrian best = null;
            float bd = float.MaxValue;
            foreach (var p in FindObjectsByType<LifeInDowntown.NPC.Pedestrian>(FindObjectsSortMode.None))
            {
                float d = (p.transform.position - spawnPosition).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) { Debug.Log("NPC_HOOK no pedestrians"); return; }
            best.mode = LifeInDowntown.NPC.Pedestrian.Mode.Phone;   // hold still for the capture
            best.GetComponentInChildren<Animator>()?.SetInteger("Mode", (int)best.mode);
            Vector3 at = best.transform.position + best.transform.forward * 3.5f;
            Vector3 to = best.transform.position - at;
            PlaceAt(new Vector3(at.x, best.transform.position.y + 0.2f, at.z), Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
            Debug.Log($"NPC_HOOK facing {best.name} at {best.transform.position}");
        }

        // value after a command-line switch, e.g. -startmission link_up
        public static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        void Update()
        {
            if (State == FlowState.Playing) playSeconds += Time.deltaTime;
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (LifeInDowntown.Map.MapController.ConsumedEscThisFrame) return;   // Esc closes the map first
            if (LifeInDowntown.Items.InventoryUI.ConsumedEscThisFrame) return;    // ... and the backpack / shop
            if (State == FlowState.Playing) Pause();
            else menu.Back();   // sub-screen -> its menu; pause menu -> resume
        }

        public void EnterMainMenu()
        {
            State = FlowState.MainMenu;
            Time.timeScale = 1f;
            player.enabled = false;
            rig.enabled = false;
            menuCamera.enabled = true;
            SetCursor(free: true);
            menu.ShowMain();
        }

        public void NewGame()
        {
            GameState.NewGame();
            playSeconds = 0f;
            ResetPhone();
            PlaceAt(spawnPosition, spawnYaw, 0f);
            StartPlaying();
        }

        public bool Load(int slot)
        {
            var data = SaveSystem.Read(slot);
            if (data == null) return false;
            GameState.Current = data.state ?? new GameState();
            LifeInDowntown.Items.Inventory.EnsureKeyItems(GameState.Current);   // saves from before the backpack
            playSeconds = data.playSeconds;
            ResetPhone();
            PlaceAt(data.position, data.yaw, data.pitch);
            StartPlaying();
            return true;
        }

        public void Save(int slot) => SaveSystem.Write(slot, Snapshot());

        // doors / elevators (World.Interactor)
        public void Teleport(Vector3 position, float yaw) => PlaceAt(position + Vector3.up * 0.1f, yaw, 0f);

        // collapsing (PlayerStats): back in the condo where a new game starts
        public void WakeAtHome() => PlaceAt(spawnPosition, spawnYaw, 0f);

        public void Pause()
        {
            State = FlowState.Paused;
            Time.timeScale = 0f;
            player.enabled = false;
            rig.enabled = false;
            SetCursor(free: true);
            menu.ShowPause();
        }

        public void Resume() => StartPlaying();

        public void QuitToMenu()
        {
            SaveSystem.Write(SaveSystem.AutoSlot, Snapshot());
            EnterMainMenu();
        }

        public void QuitToDesktop()
        {
            if (State != FlowState.MainMenu) SaveSystem.Write(SaveSystem.AutoSlot, Snapshot());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        SaveData Snapshot() => new SaveData
        {
            playSeconds = playSeconds,
            position = player.transform.position,
            yaw = player.Yaw,
            pitch = player.Pitch,
            state = GameState.Current,
        };

        // a new or loaded game starts with no call ringing and the phone put away
        static void ResetPhone()
        {
            LifeInDowntown.Phone.MissionManager.Instance?.ResetSession();
            var phone = FindFirstObjectByType<LifeInDowntown.Phone.PhoneController>();
            if (phone != null) phone.ResetPhone();
        }

        void StartPlaying()
        {
            State = FlowState.Playing;
            Time.timeScale = 1f;
            menuCamera.enabled = false;
            player.enabled = true;
            rig.enabled = true;
            menu.Hide();
            SetCursor(free: false);
        }

        void PlaceAt(Vector3 position, float yaw, float pitch)
        {
            LifeInDowntown.Vehicles.PlayerDriving.Instance?.ForceOut();   // out of the car first (load, new game, collapse)
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;                  // CharacterController overrides direct position writes
            player.transform.position = position;
            cc.enabled = true;
            player.SetLook(yaw, pitch);
            rig.Snap();
        }

        static void SetCursor(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
