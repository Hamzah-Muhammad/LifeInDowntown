# MinniGameFramework

The reusable game systems behind *Life In Downtown*: a first-person body and hands, stealth movement, guns, fist fights and enemies who fight back. It's its own Unity assembly (`MinniGameFramework.asmdef`) and namespace, and it never references game code. A game plugs in through `Core/FrameworkHooks`.

## What's in it

| Folder | Namespace | What |
|---|---|---|
| `Core/` | `MinniGameFramework.Core` | `FrameworkHooks` (everything the framework asks of a game), `IPlayerState`, `IArmory`, `SimpleState` |
| `Player/` | `MinniGameFramework.Player` | See the list below. |
| `Combat/` | `MinniGameFramework.Combat` | `WeaponHolder`: draw, aim, fire, recoil, reload with the hands, hit and panic logic |
| `NPC/` | `MinniGameFramework.NPC` | `Pedestrian` (a street person, damage), `Ragdoll` (and get-up), `NpcMelee` (staggers, knockdowns, fighting back), `Hostile` (cover, peeking, bursts, reloading, fleeing), `HostileSpawner` |

The `Player/` scripts:
- `BodycamPlayer`: walk, run, sprint, sneak, peek, noise.
- `BodycamRig`: the eye camera.
- `TarikBody`: the player's own body, two-bone IK arms, a hand frame taken from the finger bones, finger curl.
- `Fists`.
- `HurtFeedback`: red edge, glitch, tinnitus, heartbeat.
- `PlayerStats`: health, damage, regen, hunger and thirst.
- `BodycamOverlay`.

## Using it in a game

1. Put the folder in `Assets/`. It needs the Input System and HDRP packages.
2. At start-up, set the hooks you have. Anything you leave keeps a harmless default, so the framework also runs on its own (tests do that with `FrameworkHooks.Reset()`).

```csharp
FrameworkHooks.State = () => MySave.Current;        // implements IPlayerState
FrameworkHooks.Armory = new MyInventoryArmory();    // implements IArmory
FrameworkHooks.Playing = () => !MyMenu.IsOpen;
FrameworkHooks.Banner = (title, body) => MyHud.Notice(title, body);
FrameworkHooks.MakeLoot = (go, item, count) => go.AddComponent<MyPickup>().Set(item, count);
```

Install the hooks from a `[RuntimeInitializeOnLoadMethod]` for builds, and from an editor `[InitializeOnLoadMethod]` for edit-mode tests. Life In Downtown does both: `Assets/Scripts/Game/FrameworkBridge.cs` and `Assets/Editor/FrameworkBridgeEditor.cs`.

## How it's checked

Life In Downtown's `CombatTest`, `PlayerSimTest` and `ShootingTest` drive these systems step by step in edit mode. Its development range (`Assets/DevRange`, `EditorTools.DevRange.Render`) renders every pose and model before it goes into a city. See `docs/DESIGN.md` sections 3P to 3S.

## Licence

Code by Hamzah Muhammad, part of the Life In Downtown repository; it carries the repository's licence.
