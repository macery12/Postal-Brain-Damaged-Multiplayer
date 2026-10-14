# PBDMP initial multiplayer research

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Research date: 2026-10-11. Scope: two players on a LAN, direct IP/port Host and Join, movement replication, and a remote cylinder avatar. This is a movement prototype; enemies, combat, pickups, saves, and progression remain independent on each machine.

## Local game facts

The existing recovery README (`<Decompiled>/README.md`) and manifest (`<Decompiled>/manifest.json`) identify Unity **2021.3.14f1**, **IL2CPP metadata 29**, and a **Windows x64** `GameAssembly.dll`. The game executable is `<GameDir>/POSTAL Brain Damaged.exe`. No loader was present at initial inspection. `<GameDir>` and `<Decompiled>` are the [path placeholders](../README.md#path-placeholders) defined in the README. These are observations of the reference build, not a compatibility guarantee for other game builds.

The recovery contains declaration-only game types, including `Hyperstrange.PBD.Player`, `Hyperstrange.PBD.UI.Menus.MainMenu`, and `Hyperstrange.PBD.InputManager`. `MainMenu.Enable()` is a potential later integration hook. InputManager declares GAMEPLAY, MENU, and NONE states and uses Rewired. Placeholder bodies in recovered source cannot establish what those methods actually do. Inspect native disassembly or test runtime behavior before patching them. Local recovery explanation (`<Decompiled>/README.md`)

## Loader and build references

Use **BepInEx 6 Unity.IL2CPP win-x64**, not the Mono package. At research time the official bleeding-edge index lists **6.0.0-be.788+5b766a3**, dated September 1, 2026. Select `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip` from that index and pin the full version and downloaded archive hash in installation tooling. Bleeding-edge builds are development builds; compatibility still requires a successful local load. [Official build index](https://builds.bepinex.dev/projects/bepinex_be)

Extract the loader beside the game executable. Its first launch generates configuration, logs, and modding support files and can take longer than normal. Check `<GameDir>/BepInEx/LogOutput.log`, then close the game before installing the plugin. [Official IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)

Compile against the actual loader's `BepInEx/core` and first-run **`BepInEx/interop`** assemblies. BepInEx's interop manager runs Cpp2IL and the interop generator, uses Unity base libraries, and updates generated assemblies when its input hash changes. The default Unity library URL uses the detected Unity version. Offline installs need the corresponding Unity library archive staged beforehand. [Interop manager at the selected commit](https://github.com/BepInEx/BepInEx/blob/5b766a3/Runtimes/Unity/BepInEx.Unity.IL2CPP/Il2CppInteropManager.cs)

The recovery's `RecoveredDLLs` and `DummyDll` files are analysis artifacts, not runtime interop wrappers. Keep them outside the plugin's build references and never overwrite original game files with them. Local recovery explanation (`<Decompiled>/README.md`)

An IL2CPP plugin derives from `BepInEx.Unity.IL2CPP.BasePlugin` and starts in `Load()`. Its `AddComponent<T>()` adds a Unity component and automatically registers the injected type. A custom `MonoBehaviour` can own Update, UI, avatar creation, and shutdown. Include the usual `IntPtr` constructor for the native object bridge; avoid manually constructing a MonoBehaviour with `new`. [BasePlugin source](https://github.com/BepInEx/BepInEx/blob/5b766a3/Runtimes/Unity/BepInEx.Unity.IL2CPP/BasePlugin.cs), [Il2CppInterop class injection guide](https://github.com/BepInEx/Il2CppInterop/blob/master/Documentation/Class-Injection.md)

## Menu, input, and avatar approach

For the first version, create a separate runtime uGUI screen-space overlay containing **Multiplayer**, **Host**, **Join**, address, port, status, and **Disconnect**. UI elements belong under a Canvas; screen-space overlay draws above the scene. Buttons invoke their onClick UnityEvent on a completed click. Reuse the active EventSystem/input module where possible and supply a GraphicRaycaster for new UI. Avoid duplicate active input systems. [Unity Canvas documentation](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/UICanvas.html), [Button documentation](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-Button.html), [EventSystem documentation](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/EventSystem.html)

Game-specific decision: show the Multiplayer entry when the actual main menu is active; also expose an in-game session panel. Scene-name guesses alone can put UI over loading screens or cutscenes. Opening the panel must release the cursor and suspend local gameplay input; closing it must restore the previous state. Verify with the game's Rewired input path rather than assuming cursor visibility stops movement or shooting.

Use the existing local Player transform as the movement source and a visual-only remote avatar. `GameObject.CreatePrimitive(PrimitiveType.Cylinder)` creates both a renderer and collider. Disable/remove the collider so the placeholder cannot alter local physics or receive game interactions. Primitive generation can fail in stripped players, so exercise this path in the shipped game. [Unity CreatePrimitive API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/GameObject.CreatePrimitive.html)

A shader referenced only by name may be absent from a player build; `Shader.Find` can return null or lead to a pink error material. Prefer a compatible material/shader already loaded by this game, with a checked fallback. Verify remote avatar visibility in a gameplay level and camera, not just successful object creation. [Unity Shader.Find API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Shader.Find.html)

Do not clone the whole local Player for P2: its input, camera, weapon, health, inventory, and ECS registration can create competing single-player systems. Later reuse a verified third-person mesh/rig as a visual child, then implement remote animation explicitly. A first-person arms rig is not necessarily a complete body.

## Session decisions required for a useful prototype

These are implementation recommendations, not claims of existing game multiplayer support:

- One host and one joining peer. Host binds a chosen local port; Join uses the host's LAN IPv4 address and the same port. Display clear address/port validation, waiting, connected, rejected, and disconnected states. No matchmaking, relay, NAT traversal, or Internet hosting in this milestone.
- Both players manually load the same level. Include protocol/mod version and exact scene/level identity in the handshake/state. Suppress the remote avatar while either side loads, lacks a live local player, or has a level mismatch. Do not claim matching scene names also guarantee matching checkpoint/progression state.
- Exchange bounded movement snapshots at a modest fixed rate, interpolate on the Unity main thread, reject malformed/non-finite coordinates, and ignore stale sequence numbers. Socket worker code must not access Unity objects.
- Add connection and inactivity timeouts, a single-peer admission guard, and clean disconnect. Destroy remote avatars, dispose sockets, cancel background tasks, and reset stale state on disconnect, shutdown, and relevant scene transitions. Report port-in-use and connection failures in the UI/log.
- Keep network servicing and timeouts independent of game pause. Unity stops FixedUpdate and WaitForSeconds at timeScale zero; unscaledDeltaTime is unaffected by timeScale. Use Update with unscaled time or a monotonic clock for session work. Avoid forcing global timeScale to 1 merely to keep the network alive. [Unity timeScale API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Time-timeScale.html), [unscaledDeltaTime API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Time-unscaledDeltaTime.html)
- Allow the selected application/port through Windows Firewall for private LAN use when prompted. Two-PC verification is required; a loopback transport test cannot prove in-game visibility, input handling, or LAN firewall behavior.

## Acceptance evidence to collect

Record separately: loader starts and generates interop; plugin loads without exceptions; main-menu Multiplayer button opens; Host/Join validates and connects; two PCs see the other player's movement in the same level; pause/menu input behaves; wrong version/level gives useful status; disconnect/reconnect and returning to menu leave no duplicate avatars or sockets.

Keep build and transport test results separate from visual and two-PC playtest results. The initial milestone is complete only to the extent this evidence exists; multiplayer combat and shared world state require a later authority/synchronization design.
