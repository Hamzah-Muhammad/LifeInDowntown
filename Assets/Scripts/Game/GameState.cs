using System;
using System.Collections.Generic;

namespace LifeInDowntown.Game
{
    // The four hidden stats, story flags, and phone/mission progress (docs/DESIGN.md 1, 3D).
    // Carried in every save file.
    [Serializable]
    public class GameState
    {
        public string playerName = "Tarik";
        public int rep;
        public int heat;
        public int loyalty;
        public int cash = 40;
        public List<string> flags = new List<string>();

        public string activeMission = "";
        public int objectiveIndex;
        public List<PhoneMessage> messages = new List<PhoneMessage>();
        public List<string> callLog = new List<string>();

        public static GameState Current { get; set; } = new GameState();

        public static void NewGame() => Current = new GameState();

        public bool HasFlag(string f) => flags.Contains(f);
        public void SetFlag(string f) { if (!flags.Contains(f)) flags.Add(f); }
        public void ClearFlag(string f) => flags.Remove(f);
    }

    [Serializable]
    public class PhoneMessage
    {
        public string from;     // contact id
        public string text;
        public string time;     // display time, e.g. "23:41"
    }
}
