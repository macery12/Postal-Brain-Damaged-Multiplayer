# Identical players, campaigns and native-style co-op menus — phased plan

Updated October 18, 2026.

**Status: the owner confirmed the design on October 18, 2026 and authorized implementation and commits ("commit as needed"). Pushing is not authorized. All phases are complete. Released as PBDMP 1.0.0, build 960F3A0ADE93A2BB, on October 19, 2026.**

| Phase | State |
| --- | --- |
| 0 Native research | Complete |
| 1 Shared items core | Complete |
| 2 Keys, joins and saved inventory | Complete (schema change moved to Phase 3) |
| 3 Co-op campaigns (save model) | Complete |
| 4 Multiplayer menu and entry flows | Complete (Join a friend opens Direct IP until Phase 6) |
| 5 In-session pause menu and P2 view | Complete |
| 6 Join a friend | Complete (two-account join is a maintainer check) |
| 7 Hardening, docs and handoff | Complete (1.0.0) |

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

## Phase 4 handoff (October 19, 2026)

**Source**
- `NativeCoopMenu`: a copy of `MENU_Main`.
  - Its `MainMenu` is replaced by a plain `Menu`, configured like `MENU_Settings_General`, so native fades, Escape/Back and controller navigation apply.
  - The logo area becomes the MULTIPLAYER title, and the six native buttons are relabelled and wired.
  - A Continue preview shows the newest campaign: thumbnail, map name in the `HEADER_Load` lettering, difficulty, Players and time. The main-menu display font lacks punctuation glyphs, so it can't be used for names.
- `NativeCoopLoadMenu`: a copy of `MENU_Load`.
  - Two columns of the game's own `SaveHyperButton` entries.
  - Load, Delete and Back, with the native confirmation prompt.
  - Delete removes the focused save or the whole campaign.
- `MultiplayerCoopMenu`:
  - On-demand hosting: a Steam lobby, else Direct IP; the offline fixture uses Direct IP on its own port. A follow-up step runs once P1's save service exists.
  - The flows: Continue co-op, New co-op game, Load co-op game and Survival co-op (native `MENU_Horde`).
  - The main-menu entry and F8 open the menu.
- `CoopMenuPatches`: prefix on `LevelSelectionMenu.StartLevel` for co-op level select. It opens the native `PROMPT_Confirmation` with "LOAD THIS MAP AS A MULTIPLAYER GAME?"; Confirm hosts, starts a new campaign and runs native `StartLevel` (the difficulty prompt). A `MainMenu.Enable` postfix leaves co-op level select.
- `Test-Runtime -CoopMenu` (`CoopMenuDiagnostics`): a single-game fixture with real-frame captures (scene cameras plus the canvas) in an isolated diagnostic save root. The runner now moves the previous log aside, because a stale PASS line could be counted before the new process truncated the log.

**Checks**
- `-CoopMenu`, 22 checks covering:
  - menu, labels, Continue state, the preview title rendering;
  - Load lists, first selection, delete prompt and result, Back;
  - Survival co-op hosting the native Survival menu;
  - New co-op game: prompt, Cancel, Confirm hosting before the native difficulty prompt, a new campaign, the level loading;
  - a real save with its thumbnail in that campaign;
  - title, preview of that campaign, then Continue co-op hosting and loading its newest save.
- Original runtime smoke: passes, updated for the new entry and controller submit/cancel.
- Two-player: travel 54, `-SharedItems` 17, `-Saves` 45, `-JoinInput` 10.

**Limits**
- Join a friend opens the Direct IP dialog until Phase 6.
- The old lobby panel still opens from the pause menu and F8 in game until Phase 5 replaces it with Players & invite.
- Steam hosting from the menu was not exercised; the fixture uses Direct IP.
- Controller navigation inside the Load columns is configured, but only fixture clicks were exercised.

## Phase 5 handoff (October 19, 2026)

**Source**
- `NativeCoopSaveMenu`: a copy of `MENU_Save`.
  - It lists "New save" (with a live 160×90 camera thumbnail) and the campaign's manual saves.
  - Save opens the native `PROMPT_Input`, titled "NAME THIS SAVE", or the overwrite confirmation. Delete is also available.
  - Prompt callbacks never close menus; the native prompt closes itself.
- `CoopMenuPatches`:
  - `MenuManager.Open` prefix: during a co-op level session, native `MENU_Save`/`MENU_Load` open the co-op screens on P1. P2 gets a "P1 controls co-op saves and loading." notice, and Survival shows its existing refusal.
  - `PauseMenu.Enable` postfix: disables P2's Save/Load.
- `NativeCoopLoadMenu`:
  - opens on the current campaign;
  - adds **Load latest autosave** for P1 in a session (the native confirmation, then `RestoreCheckpoint`);
  - loading from inside P1's level asks "LOAD THIS SAVE? UNSAVED PROGRESS IS LOST."
- **Players & invite** (the pause entry, and F8 in game) reworks the panel:
  - *Host co-op game* (solo in a level), *Invite friend* and lobby privacy;
  - *Direct IP*: off, or "port N". While nobody has joined it re-hosts the same campaign over the other transport (`SwitchDirectHosting`, `CoopSaveService.ResumeCampaign`).
  - *Leave session* and *Back*.
  - The old Host and Co-op saves buttons are gone, and the Direct IP dialog only joins.
- **P2's Multiplayer menu while connected** is read-only (`NativeCoopMenu.ShowGuest`): "PLAYING WITH [host]", P1's map, the P1-only actions disabled, and Leave session.
- Map, campaign and player-card names use the game's own localized level names (`LevelId.Name`), e.g. "T-Day".

**Checks**
- `-CoopMenu`: 28 checks, adding:
  - pause Save → co-op Save screen; Players & invite label;
  - New save → native name prompt → a named save in the same campaign;
  - pause Load → co-op Load on the current campaign, with Load latest autosave;
  - Players & invite labels.
- Original runtime smoke: passes, updated for the new panel.
- Two-player:
  - `-Saves` 47 (P1 reaches the co-op Save screen through the native pause Save; P2's pause Save is refused);
  - `-SaveResume` 22, `-SaveReplacePartner` 23;
  - `-SharedItems` 19 (P2's read-only menu view, then restored);
  - `-JoinInput` 10, default travel 54, `-SurvivalPrep` 47.

**Limits**
- The Direct IP switch was not run, because it would create a real Steam lobby on this machine. Only its label in Direct IP hosting was checked.
- Steam hosting from the menus is a maintainer check.
- The old `NativeCoopSaves` dialog class remains unreachable from the UI. Remove it in Phase 7.

## Phase 6 handoff (October 19, 2026)

**Source**
- `NativeCoopJoinMenu`: a copy of `MENU_Load` titled "Join a friend".
  - One column of the game's own `SaveHyperButton` entries: Steam avatar, friend name, where they are ("Prison (co-op)", "Survival co-op", "In the Multiplayer menu") and "1 of 2 players", "Full" or why it cannot be joined.
  - Join, Refresh, Direct IP... (opens the existing Direct IP join dialog) and Back.
  - Rescans every 3 s while open and rebuilds only when something changed, keeping the selection and controller focus.
- `SteamService`:
  - `FriendsHosting`: immediate friends whose `GetFriendGamePlayed` is this app with a lobby. Lobby data is fetched with `RequestLobbyData` at most every 10 s.
  - The host advertises `pbdmp_level`, `pbdmp_mode` and `pbdmp_players` when they change.
  - The callback tap also queues `LobbyDataUpdate_t` (505).
- `LobbyAdvert` (PBDMP.Network): reads the advert, gives the short "cannot join" reason, and parses Steam's `+connect_lobby` launch argument.
- Join goes through `OnJoinRequested`, the same path as an accepted invite, so leaving a session or a level asks first.
- Join Game while P2's game is closed: once Steam and the main menu are ready, the `+connect_lobby` lobby is joined.
- Fix found by this phase's checks: the session's native-load guard disabled every button named `BUTTON_Load`/`BUTTON_Save`. That included PBDMP's copied screens (co-op Load, co-op Save, Load co-op game, Join) and P1's pause Save and Load, so those could not be pressed with a mouse or controller during a session (fixtures had clicked them through `onClick`).
  - The guard now skips `MENU_PBDMP_*` screens and P1's pause Save/Load.
  - `PauseMenu.Enable` sets those two buttons for P1, and greys them for P2, in a co-op level.
  - `NativeUiKit.SetButtonState` also compares the native button state, so a copied button cannot keep its source's disabled look.

**Checks**
- `Test-Steam` (one account, real Steam client, private lobby):
  - the advert;
  - `RequestLobbyData` → `LobbyDataUpdate` through the tap → a compatible advert;
  - the app id;
  - the real friend scan (68 friends, none hosting);
  - the list with a compatible and a different-version entry, Join disabled for the second;
  - Join while hosting asks first, and Stay keeps hosting;
  - the empty list, the real scan through the screen, Direct IP..., Back.
- `-CoopMenu`: 36 checks. They add Join a friend (real scan, Direct IP..., Back) and real interactability of P1's pause Save/Load, the co-op Save/Load buttons and the co-op menu while hosting.
- Original runtime smoke passes. Managed tests pass with 0 warnings, including the advert and launch-argument test.
- Two-player:
  - `-Saves` 49 (adds P1's pause Save/Load pressable and P2's greyed out);
  - `-SaveResume` 23, `-SaveReplacePartner` 24;
  - `-SharedItems` 19, `-JoinInput` 10, default travel 54, `-SurvivalPrep` 47.

**Limits**
- Two-account checks for the maintainer:
  - a friend listed and joined through Join a friend;
  - "Full" after joining;
  - an invite-only lobby not listed;
  - Join Game with P2's game closed (`+connect_lobby`).
- Steam hides invite-only lobbies from `GetFriendGamePlayed`, so they only arrive as invites.

## Phase 7 handoff: 1.0.0 release (October 19, 2026)

**Source**
- The unreachable `NativeCoopSaves` dialog and the hidden Co-op saves button are removed.
- Version 1.0.0 (protocol 13 and save schema 3 unchanged). The load log reads `PBDMP 1.0.0 build <id>`.
- `Package.ps1` writes a 1.0 `INSTALL.txt` for the new menus, Join a friend, shared items and co-op saves.
- `MissionExitDiagnostics` dismisses native tutorials (see below).

**Docs**: README (Play together, saves, version), `co-op-saves.md` (the new controls, team inventory row, world format), roadmap (current behavior, milestones 0.10 and 1.0), protocol, Steam research, sync rows, development status, and the verification release record and commands.

**Checks**: everything in the [1.0.0 release record](../verification.md#100-release--october-19-2026), run on the final DLL pair:
- 18 campaign and 21 Survival two-process fixtures;
- runtime smoke, `-CoopMenu`, the Steam probe and the managed tests.

**Findings during the release pass**
- `-ActorParts` and `-DoorReload` need `-Level LEVEL_AD_Neighborhood`, the map they test; the documented commands now say so.
- `-MissionExits -Level LEVEL_AD_Wallmarkt` timed out because Wallmarkt's native tutorial popup holds the shared pause, which also pauses P2's respawn timer. The pre-redesign build (`02740af`) failed identically. The fixture now dismisses tutorials like the other fixtures, and passes with 13 checks.

**Install and package**
- `artifacts/PBDMP-1.0.0.zip`, build 960F3A0ADE93A2BB (`PBDMP.dll` 8F48E0C20209C8E7, `PBDMP.Network.dll` 154AA4D3299BFE3F).
- Installed with `Install-Plugin.ps1` into the Steam game folder while the game was closed; the installed hashes match the zip.

**Open maintainer checks (two Steam accounts)**
- Join a friend: list, join, "Full", invite-only hidden;
- `+connect_lobby` from a closed game;
- Steam hosting from the new menus;
- the Direct IP switch in Players & invite.
