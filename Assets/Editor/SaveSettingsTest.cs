using System.IO;
using LifeInDowntown.Game;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Round-trips save files and settings through a temp folder (the real save folder is never touched).
    // Prints SAVE_PASS / SAVE_FAIL lines; exits 1 on failure.
    public static class SaveSettingsTest
    {
        public static void Run()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "LifeInDowntownTest_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.Folder = Path.Combine(tmp, "saves");
            GameSettings.FilePath = Path.Combine(tmp, "settings.json");
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "SAVE_PASS " : "SAVE_FAIL ") + what); if (!ok) fails++; }

            try
            {
                Check(SaveSystem.MostRecentSlot() == -2, "no saves at first: Continue stays disabled");
                Check(SaveSystem.Describe(0).Contains("empty"), $"empty slot label: '{SaveSystem.Describe(0)}'");

                var a = new SaveData { playSeconds = 3725f, position = new Vector3(12.5f, 0.2f, -40f), yaw = 163f, pitch = -7f,
                                       state = new GameState { cash = 120, rep = 3, heat = 1 } };
                a.state.flags.Add("answered_ty");
                SaveSystem.Write(1, a);
                var b = SaveSystem.Read(1);
                Check(b != null && b.position == a.position && Mathf.Approximately(b.yaw, 163f) && Mathf.Approximately(b.pitch, -7f),
                      "slot 2 round-trips position and look");
                Check(b != null && b.state.cash == 120 && b.state.rep == 3 && b.state.flags.Contains("answered_ty"),
                      "slot 2 round-trips stats and flags");
                Check(SaveSystem.Describe(1).Contains("1h 02m") && SaveSystem.Describe(1).Contains("$120"),
                      $"slot label shows playtime and cash: '{SaveSystem.Describe(1)}'");

                System.Threading.Thread.Sleep(20);
                SaveSystem.Write(SaveSystem.AutoSlot, new SaveData { playSeconds = 10f });
                Check(SaveSystem.MostRecentSlot() == SaveSystem.AutoSlot, "Continue picks the newest file (autosave)");
                Check(!File.Exists(SaveSystem.PathFor(1) + ".tmp"), "no leftover temp file after a write");

                File.WriteAllText(SaveSystem.PathFor(2), "{ this is not json");
                Check(SaveSystem.Read(2) == null, "a corrupt save reads as empty instead of crashing");

                GameSettings.Current.cameraShake = 0.4f;
                GameSettings.Current.horizontalFov = 92f;
                GameSettings.Current.rayTracing = false;
                GameSettings.Save();
                GameSettings.Current.cameraShake = 1f;
                GameSettings.Load();
                Check(Mathf.Approximately(GameSettings.Current.cameraShake, 0.4f) && Mathf.Approximately(GameSettings.Current.horizontalFov, 92f)
                      && !GameSettings.Current.rayTracing, "settings round-trip through settings.json");
            }
            finally
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }
            Debug.Log(fails == 0 ? "SAVE_RESULT all checks passed" : $"SAVE_RESULT {fails} check(s) failed");
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}
