using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinniGameFramework.Core
{
    // What MinniGameFramework needs from the game it runs in (docs/DESIGN.md 3S). The framework never names a
    // game type: the game installs these at start-up (LifeInDowntown: Game/FrameworkBridge). Every hook has a
    // harmless default, so framework scripts and tests also run on their own.
    public static class FrameworkHooks
    {
        static readonly SimpleState fallback = new SimpleState();

        public static Func<IPlayerState> State = () => fallback;         // the live player state (saved by the game)
        public static IArmory Armory = new NoArmory();                    // what weapon is equipped, its mag, spare rounds
        public static Func<bool> Playing = () => true;                   // false while a menu / pause / cutscene is up
        public static Func<bool> InputBlocked = () => false;             // a backpack, map or phone has the mouse
        public static Func<bool> Driving = () => false;                  // in a car: no gun, no fists
        public static Action<string, string> Banner = (title, body) => { };   // a short on-screen notice
        public static Action WakeAtHome = () => { };                      // after going down
        public static Func<float, string> Money = v => "$" + v.ToString("0.00");
        public static Func<bool> Overloaded = () => false;               // carrying too much: no sprint, slower
        public static Func<Vector3, Vector3> GroundCarry = p => Vector3.zero;   // escalators, moving walkways (m/s)
        public static Func<IList<Vector3>> SidewalkPoints = () => null;   // where people can stand (spawning)
        public static Func<Vector3, bool> NoFireZone = p => false;       // indoors: "Not here."
        public static Action<GameObject, string, int> MakeLoot = (go, item, count) => { };   // a dropped thing becomes a pickup

        public static IPlayerState S => State();

        // back to the defaults (tests that run the framework on its own)
        public static void Reset()
        {
            State = () => fallback; Armory = new NoArmory();
            Playing = () => true; InputBlocked = () => false; Driving = () => false;
            Banner = (t, b) => { }; WakeAtHome = () => { }; Money = v => "$" + v.ToString("0.00");
            Overloaded = () => false; GroundCarry = p => Vector3.zero; SidewalkPoints = () => null;
            NoFireZone = p => false; MakeLoot = (go, i, c) => { };
        }

        class NoArmory : IArmory
        {
            public int Count(string item) => 0;
            public int Magazine(string weapon) => 0;
            public string AmmoFor(string weapon) => "";
            public int Reload() => 0;
        }
    }

    // the player's live state the framework reads and writes; names match LifeInDowntown's GameState fields
    public interface IPlayerState
    {
        float health { get; set; }
        float hunger { get; set; }
        float thirst { get; set; }
        float cash { get; set; }
        int heat { get; set; }
        bool drawn { get; set; }
        int loaded { get; set; }
        string equipped { get; set; }
    }

    // the game's inventory, as far as weapons go
    public interface IArmory
    {
        int Count(string item);           // how many of an item are carried
        int Magazine(string weapon);      // its magazine size (0 = not a weapon)
        string AmmoFor(string weapon);    // the item id of its rounds
        int Reload();                     // fill the equipped weapon's mag from spare rounds; returns rounds moved
    }

    [Serializable]
    public class SimpleState : IPlayerState
    {
        public float health { get; set; } = 100f;
        public float hunger { get; set; } = 100f;
        public float thirst { get; set; } = 100f;
        public float cash { get; set; }
        public int heat { get; set; }
        public bool drawn { get; set; }
        public int loaded { get; set; }
        public string equipped { get; set; } = "";
    }
}
