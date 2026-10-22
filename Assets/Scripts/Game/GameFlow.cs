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
        }

        void Update()
        {
            if (State == FlowState.Playing) playSeconds += Time.deltaTime;
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) return;
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
            PlaceAt(spawnPosition, spawnYaw, 0f);
            StartPlaying();
        }

        public bool Load(int slot)
        {
            var data = SaveSystem.Read(slot);
            if (data == null) return false;
            GameState.Current = data.state ?? new GameState();
            playSeconds = data.playSeconds;
            PlaceAt(data.position, data.yaw, data.pitch);
            StartPlaying();
            return true;
        }

        public void Save(int slot) => SaveSystem.Write(slot, Snapshot());

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
