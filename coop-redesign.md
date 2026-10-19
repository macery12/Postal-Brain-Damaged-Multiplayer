# Identical players, campaigns and native-style co-op menus — phased plan

Updated October 18, 2026.

**Status: the owner confirmed the design on October 18, 2026 and authorized implementation and commits ("commit as needed"). Pushing is not authorized. Phase 0 is next.**

| Phase | State |
| --- | --- |
| 0 Native research | Pending |
| 1 Shared items core | Pending |
| 2 Keys, joins and saved inventory | Pending |
| 3 Co-op campaigns (save model) | Pending |
| 4 Multiplayer menu and entry flows | Pending |
| 5 In-session pause menu and P2 view | Pending |
| 6 Join a friend | Pending |
| 7 Hardening, docs and handoff | Pending |

## Start here

1. Read `AGENTS.md`, `CLAUDE.md`, `docs/agent-research-and-testing.md`, `docs/protocol.md`, `docs/co-op-saves.md` and the relevant `docs/sync-progress.md` rows.
2. Check Git status first and preserve unrelated work. Commit at useful stopping points, with no co-author or attribution lines (see `CLAUDE.md`). Do not push.
3. Work phase by phase. Update the status table and append a short handoff after each phase: source state, checks run, remaining issues and the exact next step. Do not call an untested path verified.
4. Run native fixtures sequentially on isolated copies (`.local/acceptance-host`, client `.local/travel-client`). Never close a player's running game; install only when it is closed. Keep raw logs and local paths in ignored `.local/`.
5. Public docs use `<RepoDir>`, `<GameDir>` and `<Decompiled>`. Never copy recovered game code or assets into the repository. At runtime, the menus clone or reuse the player's own native UI objects and assets.

## Confirmed design

### Part A: identical players

| Decision | Agreed behavior |
| --- | --- |
| Shared classes | **Weapons, keys and mission items** are identical for both players. Ammo, health, armor and powerup pickups stay personal, with today's per-player copies. |
| Pickups | A weapon, key or mission-item pickup exists **once**. Whichever player takes it, it disappears for both and both receive it. This includes enemy-dropped weapons, keys and items. |
| New gun | The partner receives a gun they don't have, together with the native starting ammo that the pickup carries. |
| Gun already owned | If the partner already has that gun, native rules apply only to whoever picked it up, and nothing goes to the partner. |
| Ammo | Each player fires and spends their own ammo. |
| Keys at doors | Either player opens a key door, and the key is consumed for both. |
| Dead partner | A dead partner receives shared items at once and keeps them through the three-second respawn. |
| Notice | The game's own pickup notice shows items received from the partner, with the partner's name, e.g. "+ SHOTGUN ([name])". |
| Joining | Anyone can join any campaign, with no partner-assignment prompt. A joining or rejoining player receives the team's shared weapons, keys and items, plus the existing join rules for ammo, health and armor. |
| Survival | Unchanged: each player keeps what they get. |
| Scope | Inventory deltas for the shared classes are replicated at the inventory level, so scripted grants and consumption are covered too, not only pickups. |

### Part B: menus and saves

| Decision | Agreed behavior |
| --- | --- |
| Look | Native: the main-menu text column over the purple spiral, native prompts and the native save entries. Use the game's own assets. Controller and mouse/keyboard are equal. |
| Multiplayer menu | Continue co-op · New co-op game · Load co-op game · Survival co-op · Join a friend · Back. F8 still opens it. The old lobby panel and its Host buttons are removed. |
| New co-op game | Opens the real skull level select. Start shows *"Load this map as a multiplayer game?"*, then the native difficulty prompt, then the map loads at once with a friends-only lobby. |
| Continue co-op | One press loads the newest save of the most recently played campaign. |
| Campaigns | A campaign is one continuous run of the story; only New co-op game starts another. Its name follows the latest map, e.g. "Prison (co-op)", with no renaming. Entries show difficulty, last played and a Players list. |
| Save slots | Each campaign has its own quicksave, three rotating autosaves and manual saves. Each save has a native-style screenshot thumbnail. |
| Load co-op game | Campaigns on the left; the selected campaign's saves on the right as native entries (thumbnail, Autosave/Quicksave/Manual save, level, date). Load · Delete · Back. P1 deletes single saves or whole campaigns, with the native confirmation. |
| Wording | The game's own terms: Autosave, Quicksave, Manual save, "Load latest autosave". |
| Pause menu | In a session, the native Save and Load open the co-op versions. **Players & invite** holds the player cards, Invite friend, Lobby: friends only / invite only, and Direct IP hosting. |
| Survival co-op | Opens the native arena and difficulty menu, then hosts and loads in, with no multiplayer popup. |
| Join a friend | An in-game list of Steam friends hosting PBDMP, with Join, Refresh and Direct IP… Steam invites and Join Game keep working. |
| P2 view | A read-only "Playing with [host]" view with live status; only P1 saves and loads. |
| Player count | Player lists and counts grow beyond two in layout; sync stays two-player. |
| Old saves | Development-only co-op saves may be wiped. The save schema is raised, and older files are refused with a clear message, never converted. |

Mockup: the owner's private design canvas "PBDMP co-op menu flow" (seven boards).

## Native references found so far

- **Item classes.**
  - `EntityId` has the subclasses `WeaponId` (`IsWeapon`, `AmmoTypes`, `AkimboVariant`/`NonAkimboVariant`, `NeverEquipOnPickup`, `Category`) and `ItemId` (`IsAmmo`, `IsUsable`, `PlayerPowerup`, `InventoryCapacity`).
  - Working classification: a weapon is `WeaponId`. Ammo is `ItemId.IsAmmo`. Consumables and powerups are `ItemId.IsUsable`. Keys and mission items are the remaining `ItemId`s. Phase 0 must confirm this against `PlayerInventoryComponent.RemoveKeyItems`, which uses a native key-item rule.
- **Pickups.**
  - `Pickup` defines `Interact`, `HandleInteract`, `PickupComplete`, the abstract `PickUp(Player)`, `SetEnabled`, `Drop`, `_pickedUp` and `MainItem`.
  - Concrete classes: `ItemPickup` (`_items: List<Item{EntityId,Quantity}>`, `_forcePickUp`), `ItemAndHealthPickup`, `GenericPickup`, `BagPickup`, `HealthPickup`, `ArmorPickup` and `CodexPickup` (posters, already shared).
- **Inventory.** `PlayerInventoryComponent` defines `AddItem`, which caps the quantity at `InventoryCapacity`. It also defines `RemoveItem`, `HasItem`, `GetItemQuantity`, `CollectedWeapons`, `HeldItems`, `RemoveKeyItems`, `LoadState(SaveData)` and the native `OnItemPickUp` handler.
- **Menus.**
  - `MainMenu` (Continue/Load/Codex/Mods/Credits/Exit/Leaderboards buttons).
  - `LevelSelectionMenu` (skull: `MenuSkullComponent`; `StartLevel` → `MenuManager.OpenDifficultyPrompt`).
  - `LoadMenu`/`SaveLoadMenu` (`SaveHyperButton`: `ThumbnailImage`, `TypeText`, `LevelText`, `DateText`).
  - `SaveData.Thumbnail` (`byte[]`), `PauseMenu` (`_saveButton`, `_loadButton`), `HordeModeMenu`, `ConfirmationPromptMenu`, `PromptMenu` and `MenuLegendComponent`.

## Existing implementation map

| Area | Files |
| --- | --- |
| Shared pickup precedent | `WorldPosters.cs` (host-owned collection, P2 request, current-state frame, no replayed notice on restore). |
| Loot | `SharedLoot.cs`, `SharedLootPatches.cs`, `SharedWorldPersistence.cs` (`CreatePersonalDrop`, `d:` keys). |
| Pickup history and saves | `CoopSavePatches.cs` (`PickupComplete` hook), `CoopSaveService.cs`, `CoopSaveStore`, `NativeSaveBridge.cs`. |
| Doors and keys | `SharedDoors.cs` (`RequestDoor`, requester consumption, refunds), `SharedDoorPatches.cs`. |
| Join loadout | `PlayerSessionState.cs` (P1 weapons/ammo copy on join), `SessionLifecycle.cs`. |
| Menus | `NativeMultiplayerUI.cs`, `NativeCoopSaves.cs`, `NativeUiKit.cs`, `PlayerCard.cs`, `NativePrompt.cs`, `SessionPauseMenu.cs`, `MultiplayerBehaviour.cs`. |
| Survival exclusions | `SharedSurvival.cs`, `SurvivalPatches.cs` (personal offers must stay personal). |

## Authority and ordering

- P1 owns shared-item state. P2's pickups of shared items are requests (life, epoch, distance and native `CanBeInteractedWith`), like posters. P1 commits once, and both PCs collect their own native copy of the same authored or dropped pickup.
- Inventory grants travel as bounded host-validated frames: entity asset key, quantity, reason (pickup, script, consumption) and a monotonic grant id. Apply them once, idempotently. A grant is never echoed back.
- Each PC applies a received grant through native `AddItem`/`RemoveItem` on its own player, under an "applying" guard so the inventory hook doesn't replicate it again.
- Shared classes are excluded during Survival. Personal classes are never replicated.
- Join snapshots carry the team's shared inventory. Saves store one shared inventory plus each player's personal state (health, armor, position, ammo and consumables).

## Phases and completion gates

| Phase | Work | Gate |
| --- | --- | --- |
| 0 | Confirm the item classification and key-item rule; how `ItemPickup.PickUp` grants weapons and ammo; scripted `AddItem`/`RemoveItem` callers; weapon drops; native pickup notices. For the menus: how to open `LevelSelectionMenu`, `HordeModeMenu`, `LoadMenu` and `PauseMenu` in a co-op mode; native thumbnail capture; Steam friend/lobby data for a hosting list. Record findings here. | Findings recorded with source methods; no code changes. |
| 1 | Shared items core: classification, a single shared pickup (authored and dropped), partner grant rules (new gun plus its ammo; nothing if already owned), dead partner, partner notice, Survival exclusion. Protocol bump, managed scenarios and a native fixture. | Native fixture passes in both directions; Survival offers stay personal; campaign regression passes. |
| 2 | Keys consumed for both at doors (replacing requester-only consumption and refunds where appropriate); scripted grants and removals; join/rejoin receives the team inventory; no assignment prompt; save schema 3 with a shared inventory. | Door/key, join/rejoin and save/restore fixtures pass. |
| 3 | Co-op campaigns: store layout, campaign id, map-based names, Players list, per-campaign quick/auto/manual saves, thumbnails, delete, newest-save lookup. Managed store tests. | Managed tests pass; save fixtures pass on schema 3. |
| 4 | Multiplayer menu (native column) with Continue, New (skull + prompt + difficulty), Load (campaigns + native entries), Survival co-op (native arena menu) and Back. The old lobby panel is removed; F8 still opens the menu. Controller navigation. | Native UI fixture covers every entry and route; controller navigation checked. |
| 5 | Pause menu: native Save/Load route to co-op, Players & invite (cards, invite, privacy, Direct IP hosting), P2's read-only view. | Pause and input fixtures pass; P2 cannot save or load. |
| 6 | Join a friend: Steam friends hosting PBDMP (lobby/rich-presence data), Join, Refresh, Direct IP… | One-account Steam checks pass; two-account join is a maintainer check. |
| 7 | Hardening, the full campaign and Survival regressions, README/protocol/co-op-saves/sync rows, package, install when the game is closed, handoff. | Regressions pass; docs updated. |

## Current handoff

Plan written. Next step: Phase 0 native research.
