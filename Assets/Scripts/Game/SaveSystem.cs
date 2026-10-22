using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace LifeInDowntown.Game
{
    // Save files (docs/DESIGN.md 3C): three manual slots plus one autosave, as JSON under
    // <persistentDataPath>/saves/. Continue loads whichever file was written most recently.
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string savedAtUtc;          // ISO 8601
        public float playSeconds;
        public Vector3 position;
        public float yaw;
        public float pitch;
        public GameState state = new GameState();

        public DateTime SavedAt =>
            DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime() : DateTime.MinValue;
    }

    public static class SaveSystem
    {
        public const int ManualSlots = 3;
        public const int AutoSlot = -1;

        // overridable so tests never touch the real save folder; lazy because Unity forbids
        // persistentDataPath in static initialisers
        static string folderOverride;
        public static string Folder
        {
            get => folderOverride ?? Path.Combine(Application.persistentDataPath, "saves");
            set => folderOverride = value;
        }

        public static string PathFor(int slot) =>
            Path.Combine(Folder, slot == AutoSlot ? "autosave.json" : $"slot_{slot + 1}.json");

        public static void Write(int slot, SaveData data)
        {
            Directory.CreateDirectory(Folder);
            data.savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string path = PathFor(slot), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));   // write-then-rename: a crash never leaves half a file
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static SaveData Read(int slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning($"Unreadable save {path}: {e.Message}"); return null; }
        }

        public static void Delete(int slot)
        {
            string path = PathFor(slot);
            if (File.Exists(path)) File.Delete(path);
        }

        // most recently written save of all slots including the autosave, or -2 when there is none
        public static int MostRecentSlot()
        {
            int best = -2;
            DateTime bestTime = DateTime.MinValue;
            for (int s = AutoSlot; s < ManualSlots; s++)
            {
                var d = Read(s);
                if (d != null && d.SavedAt > bestTime) { bestTime = d.SavedAt; best = s; }
            }
            return best;
        }

        public static string Describe(int slot)
        {
            var d = Read(slot);
            string name = slot == AutoSlot ? "Autosave" : $"Slot {slot + 1}";
            if (d == null) return $"{name}  —  empty";
            var t = TimeSpan.FromSeconds(d.playSeconds);
            return $"{name}  —  {d.SavedAt:MMM d, HH:mm}  ·  played {(int)t.TotalHours}h {t.Minutes:00}m  ·  ${d.state.cash}";
        }
    }
}
