# Identical players, campaigns and native-style co-op menus — phased plan

Updated October 18, 2026.

**Status: the owner confirmed the design on October 18, 2026 and authorized implementation and commits ("commit as needed"). Pushing is not authorized. Phases 0–3 are complete; Phase 4 is next.**

| Phase | State |
| --- | --- |
| 0 Native research | Complete |
| 1 Shared items core | Complete |
| 2 Keys, joins and saved inventory | Complete (schema change moved to Phase 3) |
| 3 Co-op campaigns (save model) | Complete |
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

## Phase 0 findings (October 18, 2026)

Native facts come from the recovered ISIL of the reference build. Runtime facts come from the `-Audit` pickup census over all 15 campaign maps, which writes `pickups-<level>.tsv`.

- **Grant path.**
  - `ItemPickup.PickUp(player)` invokes the `Player.OnItemPickUp(EntityId,int)` delegate (`[player+0x50]`) for each `_items` entry.
  - `PlayerInventoryComponent.OnItemPickUp` only calls `AddItem`.
  - `AddItem` caps the quantity at `InventoryCapacity`, then calls `ItemsInventory.AddItem` or `WeaponsInventory.AddWeapon`, shows the tutorial, and fires an inventory event.
  - `RemoveItem` mirrors this. `AddItem` is called directly only from inside the inventory; other sources go through the delegate.
  - Keys are consumed through `Player.RemoveItem`, and `LevelController.EndLevel` calls `RemoveKeyItems`.
- **Key-item rule.** `RemoveKeyItems` keeps a held `ItemId` as a key item when `_isAmmo` (`+0x39`) is false and the virtual `IsUsable` is false. This matches the planned shared class.
- **Exception.** `HEALTH_*` and `ARMOR_*` ids pass the same test, but they appear only in `HealthPickup`/`ArmorPickup`. The shared rule is therefore limited to `ItemPickup` contents.
- **Weapons.** Shared weapons are those with a `_pickupPrefab`. That excludes unarmed, the dong, drink variants and the cutscene weapon. A weapon pickup bundles the gun, its akimbo variant and its native ammo; for example, Shotgun = `WEAPON_Shotgun`, `WEAPON_Shotgun_Akimbo` and `AMMO_Shells`×20.
- **Census.**
  - Shared item pickups in the campaign: weapons on Neighborhood, Prison, Hospital, ComicCon, SozebTrain and VIPZone.
  - Keys: `KEY_*`, `ILLUMINATI_*` and `KEY_Lever`/`Winch`/`TNT`/`Square`/`Eye`/`Sozeb*`.
  - Personal: ammo, `HEALTH_Pipe`, `AMMO_BOX`, `AKIMBO`, drinks, `PORTABLE_ARMOR`, `QUICK_PILLZ` and `STEROIDS` (usable), plus `HealthPickup`/`ArmorPickup`/`BagPickup`.
  - Native `LootController` drops only health, ammo and bags. Enemies don't drop weapons or keys through it.
- **Pickup lifecycle.**
  - `HandleInteract` calls the virtual `PickUp`, sets `_isBeingPickedUp`, then runs `PickupComplete` directly or after a tween.
  - `PickupComplete` sets `_pickedUp`, hides the object and fires the pickup and interaction listeners.
  - `SetEnabled(true)` marks the pickup taken and hides it without firing listeners.
- **Menus.**
  - `MenuManager.Open(Menu)` keeps the native stack and Escape handling.
  - `OpenConfirmationPrompt(Action, LocalizedString)`, `OpenDifficultyPrompt(Action)`, `OpenInputPrompt` and `OpenPleaseWaitPrompt` are available.
  - `LevelSelectionMenu.StartLevel` opens the difficulty prompt.
  - The main, pause and Horde menu instances are `MenuManager` fields.
- **Thumbnails.** The save coroutine renders `Player.RenderToRenderTexture` into a texture through `Texture2D.ReadPixels` and stores PNG bytes from `EncodeToPNG`. `SaveHyperButton` shows them through `ImageConversion.LoadImage` → `Sprite.Create`.
- **Steam.** PBDMP calls the flat Steam API. A hosting list needs `ISteamFriends` `GetFriendCount`/`GetFriendByIndex`/`GetFriendGamePlayed`, which reports a friend's lobby ID, plus `RequestLobbyData`/`GetLobbyData` for the build and map.
- **Left for Phase 4:** which native objects the main-menu Story and Load buttons open, and the save/load submenu instances behind the pause menu's Save and Load buttons. Resolve these at runtime when building the menus.

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

## Phase 1–2 handoff (October 19, 2026)

**Source**
- Protocol 13 (`SharedItemsProtocol`).
- `SharedItems.cs`: classification; single shared pickups, authored and dropped; partner grants; team inventory; partner notices.
- `SharedItemDiagnostics.cs` (`-SharedItems`).
- P2 key doors consume the team key natively on P1 (`SharedDoorPatches.Requirements`).
- No partner-assignment prompt; a different player joins fresh (`CoopSaveService.Assigned`).
- Campaign respawn keeps shared-class holdings changed while dead (`PlayerSessionState.KeepSharedSinceDeath`).
- The `-Doors` and replace-partner fixtures were updated to the new rules.
- The campaign audit now writes the `pickups-<level>.tsv` census.

**Checks**, on an isolated copy:
- Managed: 0 warnings; network scenarios and 8 reporting checks pass, including the shared-item protocol scenario.
- Native: `-SharedItems -Level LEVEL_AD_Prison` 17; `-Doors` 20; `-Combat` 28; default travel 54; `-Saves`, `-SaveResume` and `-SaveResume -SaveReplacePartner` on Neighborhood 42 / 21 / 22; `-JoinInput` 10; `-SurvivalPrep` (Suburbs) 47, so Survival offers stay personal.

**Limits**
- Fixture pickups are taken by native `Interact` after teleporting P2 onto the pickup. A natural walk-over is a maintainer check.
- A late join receiving keys uses the same snapshot team frame that the fixture exercises for weapons, but has no dedicated check.
- Scripted (non-pickup) grants on P1 follow through the team inventory. No native scripted-grant route was exercised.
- Saves still store P2's personal record, which includes its keys. The team inventory overrides them on restore. Schema 3 (Phase 3) drops them.

## Phase 3 handoff (October 19, 2026)

**Source**
- `CoopSave` schema 3 adds `Guests` (one record per player, at most 8, unique identities) and `Players` (display names, at most 8).
- `CoopSaveStore`:
  - per-campaign folders: `CampaignFolder`, `Campaigns`, `NewCampaignId`, `DeleteCampaign`;
  - `.info` sidecar listings, with a fallback to a full read when the sidecar is stale;
  - a `.png` thumbnail checked for its PNG signature and limited to 2 MB;
  - `Delete(slot)`;
  - schemas 1 and 2 are refused with `OlderFormat`.
- `CoopSaveService`:
  - holds a current campaign: a fixed `DiagnosticCampaign` for fixtures, otherwise a new ID, `StartNewCampaign`, or `RequestLoad(campaign, slot)`;
  - adds `ContinueLatest`;
  - keeps per-identity guest records and a Players list;
  - captures the native 160×90 thumbnail with `Player.RenderToRenderTexture` and `ReadPixels`.
- The old Co-op saves dialog lists the current campaign until Phase 4 replaces it.

**Checks**
- Managed: 0 warnings. New scenarios cover campaign listing, thumbnails, per-player records and deletion, and older schemas being refused.
- Native: `-Saves` 45 (adds the thumbnail, sidecar Players and campaign folder checks), `-SaveResume` 21, `-SaveReplacePartner` 22, `-Props` 66, `-Scenery` 19, `-SharedItems` 17.

**Limits**
- The map-based campaign name ("Prison (co-op)") is formatted in Phase 4's UI from the newest save's level.
- Deleting from the UI is Phase 4.

## Current handoff

Next step: Phase 4, the native-style Multiplayer menu and entry flows.
