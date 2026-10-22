using System;
using System.Collections.Generic;

namespace LifeInDowntown.Game
{
    // The four hidden stats and story flags (docs/DESIGN.md 1). Carried in every save file.
    [Serializable]
    public class GameState
    {
        public string playerName = "Malik";
        public int rep;
        public int heat;
        public int loyalty;
        public int cash = 40;
        public List<string> flags = new List<string>();

        public static GameState Current { get; set; } = new GameState();

        public static void NewGame() => Current = new GameState();
    }
}
