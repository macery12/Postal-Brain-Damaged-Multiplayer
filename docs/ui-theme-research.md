# Native UI theme research

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

Research date: October 12, 2026. Scope: replace PBDMP's plain injected multiplayer button and connection controls with UI built from POSTAL: Brain Damaged's installed assets and native interaction components.

## Result

The installed game already contains the required building blocks: its exact main-menu button component and font/material, a decorated prompt panel with a spiral, native action buttons, a styled TMP input field, native navigation, and hover/click sounds. New generated artwork is unnecessary for an initial polish pass. These are verified serialized asset and native method findings; sizing, controller focus, rendering, and reopening behavior still require an in-game test.

## Verified asset inventory

The following identifiers refer to the reference build's serialized files, not stable public asset IDs. Source paths below use the `<GameDir>` and `<Decompiled>` [path placeholders](../README.md#path-placeholders). Resolve components from the live `MenuManager.MainCanvas` hierarchy at runtime rather than hardcoding these numeric IDs.

| Element | Installed asset / hierarchy | Verified implementation |
| --- | --- | --- |
| Main menu | `resources.assets`, GameObject 1395 `MENU_Main` | `MainMenu` component 10650 and `CanvasGroup`; full-canvas anchors |
| Main button stack | `MENU_Main/ButtonsLayout`, GameObject 2714 | `VerticalLayoutGroup`; children are ordinary native menu entries |
| Main button template | `MENU_Main/ButtonsLayout/BUTTON_Start`, GameObject 525 | `MainHyperButton` 12449 plus `LocalizedText` 11315 on the same object; 900 × 80 reference units |
| Alternative template | `MENU_Main/ButtonsLayout/BUTTON_Settings`, GameObject 810 | `MainHyperButton` 12110 and `LocalizedText` 11586; same visual structure |
| Pause button stack | `MENU_Pause`, GameObject 2627; `BUTTON_Settings` 2407 | Native `PauseMenu`; settings button 700 × 80 reference units. Pause navigation has its own update routine |
| Connection prompt shell | `PROMPT_Input`, GameObject 3139 | Native `InputPromptMenu` 12455; `OverlayImage/Background` contains the panel, spiral, text, input, Confirm and Cancel |
| Decorated panel | `sharedassets0.assets`, Sprite 6526 `UI_Panel` | 1349 × 725 source rect; sliced border left/bottom/right 65, top 180. Native Image uses Sliced mode |
| Dim overlay | `sharedassets0.assets`, Sprite 6493 `UI_GradientOverlay` | 2048 × 1152 source rect |
| Spiral | `sharedassets0.assets`, Sprite 6652 `UI_Spiral` | Native prompt uses a faint Image and `LocalRotation`; no new art needed |
| Action backing | `resources.assets`, Sprite 260 `UI_BigButton` | Used under prompt Confirm/Cancel text; action template includes padding/layout helpers |
| Text input | `PROMPT_Input/OverlayImage/Background/INPUT_Field/Image/InputField (TMP)` | GameObject 3131; `TMP_InputField` 12167 with `Text Area`, `RectMask2D`, TMP placeholder and text components |
| Clickable font | `sharedassets0.assets`, TMP font asset 60698 `FONT_Clickable` | Native main-menu labels use this font and Material 2176 `FONT_Clickable Material` |
| Input font | TMP font asset 60699 `FONT_InputText` | Native input text uses Material 2197 `FONT_InputText Material` |
| Nonclickable / header font | TMP font asset 60700 `FONT_NotClickable` | Native settings headers and placeholder use this family; Material 21 `FONT_NotClickable Material` |
| Body font | TMP font asset 60701 `FONT_Text` | Native prompt explanatory text; Material 2191 `FONT_Text Material` |
| Canvas | `MenuManager/Canvas`, GameObject 1649 | Overlay canvas, sorting order 2, native raycaster and scaler; scaler reference resolution 3840 × 2160, height match 1 |

Source: direct UnityPy reads of resources.assets (`<GameDir>/POSTAL Brain Damaged_Data/resources.assets`), sharedassets0.assets (`<GameDir>/POSTAL Brain Damaged_Data/sharedassets0.assets`), and script references in globalgamemanagers.assets (`<GameDir>/POSTAL Brain Damaged_Data/globalgamemanagers.assets`). Raw inspection output is not distributed. No extracted game artwork is included in the mod source or package.

## Native interaction behavior

`MainHyperButton` derives from the game's `HyperButton`, which derives from Unity `Button`. It already implements pointer enter/exit/down/up, selection and submit. Its native `Select`, `Deselect`, `Click`, `Unclick`, `Submit`, and reset methods call `Extension_UI`, which applies text color and transform scale tweens. `SetInteractable` selects `ClickableMaterial`; checked state selects `NotClickableMaterial`. Retaining this component preserves the genuine native interaction style instead of approximating it with new color tables.

Sources: MainHyperButton declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/UI/Components/MainHyperButton.cs`) and native disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/UI/Components/MainHyperButton.txt`); HyperButton declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/UI/Components/HyperButton.cs`) and native disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/UI/Components/HyperButton.txt`).

`Extension_UI.PlayClickSound` calls FMOD `RuntimeManager.PlayOneShot("event:/UI/Click", …)`. `PlaySelectSound` uses `event:/UI/Select`. `MainHyperButton.Select` calls the select sound; `HyperButton.OnClick` calls the click sound. Reusing native components gets these sounds automatically. Rebuilt controls can call these public helper methods explicitly, but should not add an extra sound to an already native button.

Sources: Extension_UI declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/Extension_UI.cs`) and native disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/Extension_UI.txt`).

The main menu's serialized buttons use explicit Unity navigation references. Merely inserting a cloned sibling will leave its copied up/down links pointing at the source neighbors and will leave the neighbors skipping the new button. Build or patch the complete local up/down chain, including disabled entries, and restore any edited native links if the injection is removed. PauseMenu also calls its own `UpdateNavigation` routine, so pause entry links need validation after each enable/state refresh. Unity's navigation documentation confirms Explicit mode uses manually assigned neighbors.

Sources: installed main-menu component data above; PauseMenu declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/UI/Menus/PauseMenu.cs`); [Unity Selectable navigation](https://docs.unity3d.com/Packages/com.unity.ugui@1.0/manual/script-SelectableNavigation.html).

## Safe reuse and lifecycle

1. Wait for the live native `MenuManager`/canvas to exist. Find `MainMenu` and the exact local button template beneath its `ButtonsLayout`. A source found through `Resources.FindObjectsOfTypeAll` may instead be an inactive asset object; check that it belongs to the live menu.
2. Instantiate only the needed button/control/shell under an inactive staging parent. Unity clones descendants and components and defers Awake/OnEnable for objects that are not active in the hierarchy. Reparent after replacing original actions and inherited navigation; activate last. Do not copy `MainMenu`, `InputPromptMenu`, or `MenuManager` into an independent overlay unchanged.
3. Replace the clone's `onClick` with a new `Button.ButtonClickedEvent`, then add PBDMP's action. `RemoveAllListeners` only removes nonpersistent listeners, so it does not safely detach the serialized Start/Settings/Confirm/Cancel action. The serialized input events also need replacement before reusing input controls.
4. Clear each clone's `LocalizedText.LocalizedLabel` and assign the multiplayer label directly, or supply a PBDMP-owned `LocalizedString`. Native `LocalizedText.SetText` checks whether its label exists and returns without overwriting text when null. Leaving the original label attached would reset `Multiplayer` back to `Start`/`Settings` on enable or language changes.
5. Preserve font assets, font materials, sprites, raycast configuration, layouts, masking, and `MainHyperButton`. Changing shared font materials globally would recolor the entire native UI; clone a material only if customization actually requires it.
6. Native `Menu` plus `MenuManager.Open/Close` is an available integration seam for a true multiplayer page. These manage the previous window, canvas interactivity, selection, fade/spiral transitions, and menu stack. A separate themed modal can instead reuse the assets while retaining PBDMP's existing input ownership, but must prevent the underlying native menu from receiving clicks or escape/controller actions.
7. Main-menu and pause-menu injections must be idempotent and recreated only if their owning native object is destroyed. A stable name/marker and retained reference prevents duplicate entries on every menu enable.

Sources: [Unity Object.Instantiate](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Object.Instantiate.html); [Unity RemoveAllListeners](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Events.UnityEventBase.RemoveAllListeners.html); LocalizedText native disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/LocalizedText.txt`); Menu declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/UI/Menus/Menu.cs`); MenuManager declarations (`<Decompiled>/CSharp/Assembly-CSharp/Hyperstrange/PBD/MenuManager.cs`) and native disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/MenuManager.txt`).

## Adopted UI design

Multiplayer appears below Survival Mode. A decorated native dialog contains IP/port/status/Host/Join/Disconnect/Back. A matching pause entry and F8 provide in-game access. The native assets cover the design without generated artwork.

## Runtime uncertainties and checks

Version 0.4 runtime inspection and implementation validate the live paths, fonts and sprites above. New cloned UnityEvents contain no persistent actions; native label refresh keeps the multiplayer strings; repaired navigation includes the new entry and reopen creates no duplicate. Pointer callbacks and the native controller-submit branch open the dialog. The latter is gated by `InputManager.JoystickEnabled` in `HyperButton.OnSubmit`, so the test temporarily sets that flag and restores it. Physical device tests remain separate. Native button submit disassembly (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/UI/Components/HyperButton.txt`), UI diagnostics (`src/PBDMP.Plugin/NativeUiDiagnostics.cs`)

MenuManager.OnEnable/OnDisable subscribe/unsubscribe only its joystick-change and menu-cancel delegates in the reference build. The modal temporarily disables that component, blocks MainCanvasGroup interactivity/raycasts and installs its own cancel delegate, then restores the saved state. Original menus remain in their existing stack. Fixed-size prompt action layout helpers are removed from clones because PBDMP places them explicitly. The normal MENU input mode preserves UI mappings while suppressing gameplay. Runtime checks validate manager/group restoration and cancel behavior. MenuManager native lifecycle (`<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/Assembly-CSharp/Hyperstrange/PBD/MenuManager.txt`), NativeMultiplayerUI implementation (`src/PBDMP.Plugin/NativeMultiplayerUI.cs`)

Asset presence does not prove the final layout. Test an injected main entry with actual pointer and keyboard/controller selection, confirm native hover/click behavior, ensure clicking never invokes the source action, and confirm label stability across reopen/language refresh. Test IP/port editing and Tab/Enter/Escape without invoking game shortcuts. Check that Host/Join/Disconnect state remains legible, menu back behavior restores the previous native selection, F8 input ownership remains balanced, and repeated open/close creates no duplicates. Render the final controls at 1280 × 720 and higher/wider aspect ratios; native reference units differ from PBDMP's current generic canvas.

Recovered C# files contain declarations and throw placeholders, not recovered logic. Behavioral claims above were checked against native disassembly where necessary. No game startup, installation, or production code changes were performed during this research.
