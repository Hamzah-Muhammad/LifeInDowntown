using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeInDowntown.Game
{
    // Drives Assets/UI/Menu.uxml: main menu, pause menu, save/load slots, settings (docs/DESIGN.md 3C).
    [RequireComponent(typeof(UIDocument))]
    public class MenuController : MonoBehaviour
    {
        public GameFlow flow;

        VisualElement root, main, pause, slots, settings, current, returnTo;
        Label slotsTitle, toast;
        Button continueBtn, slotAuto;
        readonly Button[] slotBtns = new Button[SaveSystem.ManualSlots];
        bool slotsForSave;
        float toastUntil;
        List<Resolution> resolutions;

        static readonly FullScreenMode[] DisplayModes =
            { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
        static readonly List<string> DisplayNames = new List<string> { "Borderless fullscreen", "Exclusive fullscreen", "Windowed" };

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            main = root.Q("screen-main");
            pause = root.Q("screen-pause");
            slots = root.Q("screen-slots");
            settings = root.Q("screen-settings");
            slotsTitle = root.Q<Label>("slots-title");
            toast = root.Q<Label>("toast");
            continueBtn = root.Q<Button>("btn-continue");

            continueBtn.clicked += () => { int s = SaveSystem.MostRecentSlot(); if (s != -2) flow.Load(s); };
            Click("btn-new", () => flow.NewGame());
            Click("btn-load", () => OpenSlots(save: false));
            Click("btn-settings", OpenSettings);
            Click("btn-quit", () => flow.QuitToDesktop());
            Click("btn-resume", () => flow.Resume());
            Click("btn-save", () => OpenSlots(save: true));
            Click("btn-pause-load", () => OpenSlots(save: false));
            Click("btn-pause-settings", OpenSettings);
            Click("btn-quit-menu", () => flow.QuitToMenu());
            Click("btn-quit-desktop", () => flow.QuitToDesktop());
            Click("btn-slots-back", Back);
            Click("btn-settings-back", Back);

            slotAuto = root.Q<Button>("slot-auto");
            slotAuto.clicked += () => SlotClicked(SaveSystem.AutoSlot);
            for (int i = 0; i < slotBtns.Length; i++)
            {
                int slot = i;
                slotBtns[i] = root.Q<Button>($"slot-{i}");
                slotBtns[i].clicked += () => SlotClicked(slot);
            }
            BindSettings();
        }

        void Click(string name, System.Action action) => root.Q<Button>(name).clicked += action;

        void Update()
        {
            if (toast != null && toastUntil > 0f && Time.unscaledTime > toastUntil) { toast.text = ""; toastUntil = 0f; }
        }

        // ----------------------------------------------------------------- screens
        public void ShowMain()
        {
            continueBtn.SetEnabled(SaveSystem.MostRecentSlot() != -2);
            returnTo = main;
            Show(main);
        }

        public void ShowPause()
        {
            returnTo = pause;
            Show(pause);
        }

        public void Hide() => root.style.display = DisplayStyle.None;

        // Esc / Back: sub-screens return to the menu they came from; the pause menu resumes
        public void Back()
        {
            if (current == slots || current == settings) Show(returnTo);
            else if (current == pause) flow.Resume();
        }

        void Show(VisualElement screen)
        {
            root.style.display = DisplayStyle.Flex;
            foreach (var s in new[] { main, pause, slots, settings })
                s.style.display = s == screen ? DisplayStyle.Flex : DisplayStyle.None;
            current = screen;
            screen.Q<Button>()?.Focus();
        }

        void Toast(string message)
        {
            toast.text = message;
            toastUntil = Time.unscaledTime + 2.5f;
        }

        // ------------------------------------------------------------------- slots
        void OpenSlots(bool save)
        {
            slotsForSave = save;
            slotsTitle.text = save ? "Save Game" : "Load Game";
            RefreshSlots();
            Show(slots);
        }

        void RefreshSlots()
        {
            slotAuto.style.display = slotsForSave ? DisplayStyle.None : DisplayStyle.Flex;   // the autosave is never written by hand
            slotAuto.text = SaveSystem.Describe(SaveSystem.AutoSlot);
            slotAuto.SetEnabled(SaveSystem.Read(SaveSystem.AutoSlot) != null);
            for (int i = 0; i < slotBtns.Length; i++)
            {
                slotBtns[i].text = SaveSystem.Describe(i);
                slotBtns[i].SetEnabled(slotsForSave || SaveSystem.Read(i) != null);
            }
        }

        void SlotClicked(int slot)
        {
            if (slotsForSave)
            {
                flow.Save(slot);
                RefreshSlots();
                Toast($"Saved to slot {slot + 1}");
            }
            else if (!flow.Load(slot)) Toast("That save could not be loaded");
        }

        // ---------------------------------------------------------------- settings
        DropdownField quality, resolution, display;
        Toggle vsync, rayTracing, motionBlur;
        Slider fov, shake, sensitivity, volume;

        void BindSettings()
        {
            quality = root.Q<DropdownField>("set-quality");
            resolution = root.Q<DropdownField>("set-resolution");
            display = root.Q<DropdownField>("set-display");
            vsync = root.Q<Toggle>("set-vsync");
            rayTracing = root.Q<Toggle>("set-raytracing");
            motionBlur = root.Q<Toggle>("set-motionblur");
            fov = root.Q<Slider>("set-fov");
            shake = root.Q<Slider>("set-shake");
            sensitivity = root.Q<Slider>("set-sensitivity");
            volume = root.Q<Slider>("set-volume");

            quality.choices = new List<string>(QualitySettings.names);
            display.choices = DisplayNames;
            resolutions = GameSettings.Resolutions();
            resolution.choices = resolutions.ConvertAll(r => $"{r.width} x {r.height}");

            quality.RegisterValueChangedCallback(e => Change(s => s.quality = quality.index));
            resolution.RegisterValueChangedCallback(e => Change(s =>
            {
                if (resolution.index < 0 || resolution.index >= resolutions.Count) return;
                s.width = resolutions[resolution.index].width;
                s.height = resolutions[resolution.index].height;
            }));
            display.RegisterValueChangedCallback(e => Change(s => s.displayMode = DisplayModes[Mathf.Max(0, display.index)]));
            vsync.RegisterValueChangedCallback(e => Change(s => s.vsync = e.newValue));
            rayTracing.RegisterValueChangedCallback(e => Change(s => s.rayTracing = e.newValue));
            motionBlur.RegisterValueChangedCallback(e => Change(s => s.motionBlur = e.newValue));
            fov.RegisterValueChangedCallback(e => Change(s => s.horizontalFov = e.newValue));
            shake.RegisterValueChangedCallback(e => Change(s => s.cameraShake = e.newValue));
            sensitivity.RegisterValueChangedCallback(e => Change(s => s.mouseSensitivity = e.newValue));
            volume.RegisterValueChangedCallback(e => Change(s => s.masterVolume = e.newValue));
        }

        void OpenSettings()
        {
            var s = GameSettings.Current;
            quality.SetValueWithoutNotify(quality.choices[Mathf.Clamp(s.quality, 0, quality.choices.Count - 1)]);
            int w = s.width > 0 ? s.width : Screen.currentResolution.width;
            int h = s.height > 0 ? s.height : Screen.currentResolution.height;
            int ri = resolutions.FindIndex(r => r.width == w && r.height == h);
            if (ri >= 0) resolution.SetValueWithoutNotify(resolution.choices[ri]);
            display.SetValueWithoutNotify(DisplayNames[Mathf.Max(0, System.Array.IndexOf(DisplayModes, s.displayMode))]);
            vsync.SetValueWithoutNotify(s.vsync);
            rayTracing.SetValueWithoutNotify(s.rayTracing);
            motionBlur.SetValueWithoutNotify(s.motionBlur);
            fov.SetValueWithoutNotify(s.horizontalFov);
            shake.SetValueWithoutNotify(s.cameraShake);
            sensitivity.SetValueWithoutNotify(s.mouseSensitivity);
            volume.SetValueWithoutNotify(s.masterVolume);
            Show(settings);
        }

        // every change applies immediately and is written to settings.json
        static void Change(System.Action<SettingsData> edit)
        {
            edit(GameSettings.Current);
            GameSettings.Apply();
            GameSettings.Save();
        }
    }
}
