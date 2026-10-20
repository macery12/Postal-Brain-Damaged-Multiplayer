# README captures

The README gallery uses PBDMP 1.0.1, captured at 1920×1080 in two visible, windowed isolated game copies over Direct IP. Raw PNGs, logs, process IDs and saves stay under `<RepoDir>/.local/`. Final JPGs are downscaled with Lanczos filtering at quality 85, with metadata stripped.

## Capture isolation

With the owner's game closed, build against the isolated host's real loader/interop and copy the matching plugin DLL pair into its private plugin folder. Do not package or install this capture build as the release.

```powershell
./scripts/Test-Travel.ps1 -GameDir '<RepoDir>/.local/acceptance-host' -Visible -Width 1920 -Height 1080 -ReadmeCapture
```

`-Visible`, `-Width` and `-Height` are opt-in; existing travel fixtures keep their hidden 1280×720 defaults. `-ReadmeCapture` requires visible windows and the isolated host path. The runner starts P1, then P2 in `.local/travel-client`, records their PIDs and stops only its own processes. Each run gets fresh, separate save roots. It does not seed campaigns.

The capture environment suppresses local Steam names/avatars in player cards and keeps Direct IP P1/P2 identities. Before title initialization, it blocks `LeaderboardManager.UploadTime`, `SteamLeaderboard.UploadScore` and `SteamUserStats.UploadLeaderboardScore`: the title scene can otherwise submit existing solo times before hosting. The runner requires the isolation startup message before launching P2. Review both current Unity logs for uploads after every run.

## Staging controls

Write a UTF-8 JSON object to `<output>/<host|client>-command.json`, using a temporary file followed by a rename. Wait for `<role>-command-result.txt`; delete the previous result before each command. The fixture consumes commands on Unity's main thread. `<role>-capture-state.txt` records readiness, current map, position and health. A successful `shot` queues `ScreenCapture.CaptureScreenshot` at the end of the real rendered frame; wait for the PNG to finish writing before reading it.

Examples:

```json
{"action":"multiplayer"}
{"action":"compact-preview"}
{"action":"shot","name":"multiplayer-menu.png"}
{"action":"load","level":"LEVEL_AD_Neighborhood"}
{"action":"inspect"}
{"action":"teleport","x":198,"y":8.5,"z":43,"pitch":2,"yaw":347}
{"action":"fire","enabled":true}
{"action":"burst","name":"campaign"}
{"action":"fire","enabled":false}
{"action":"save","slot":"manual-park","name":"Into the park"}
{"action":"finish"}
```

`inspect` writes active authored NPC/pickup positions. `pickup` takes an instance `id` and uses native `Pickup.Interact`; it does not add inventory. `fire` invokes the equipped weapon's native attack animation, including its native callbacks. `burst` queues ten frames 0.2 seconds apart. `skip` uses the native host cutscene close action; loading gates and tutorial prompts are continued automatically. Finish both roles to let the runner clean up.

Other menu actions are `new`, `start`, `confirm` (with `menu`, e.g. `PROMPT_Difficulty`), `back`, `pause`, `players`, `resume`, `title`, `load-menu` and `friend`. `click` invokes a native button at a `path` below `MainCanvasGroup`; `inspect-ui` records those paths; `hide` disables a named panel for capture. `restart` uses the native same-map restart.

## Gallery staging

| Image | Scene and staging |
| --- | --- |
| `multiplayer-menu.jpg` | Native title-scene wide room camera, with the Dude visible on the couch. The live Continue preview uses an actual Neighborhood save, native thumbnail, difficulty and P1/P2 names. Capture-only `compact-preview` scales the preview to 65% and puts it below the Dude; the normal preview obscures him. The title camera is unchanged. |
| `campaign-coop.jpg` | Neighborhood park, P1 and P2 with the native pistol pickup, firing through native attack callbacks at an authored Golden Retriever enemy. Players were positioned for the shot; enemies were neither spawned nor frozen. |
| `survival-coop.jpg` | Suburbs arena, native start shotgun/readiness and a live host-owned wave. Players were positioned for the shot; enemies spawned through the native wave controller. |
| `new-coop-game.jpg` | Native level select with Neighborhood selected and the exact multiplayer confirmation prompt. The native leaderboard panel is hidden for capture so it cannot disclose a real Steam name. |
| `load-coop-game.jpg` | Native co-op browser with real Neighborhood and Prison campaigns, real saves and camera thumbnails. No menu-fixture campaigns are used. |
| `join-a-friend.jpg` | Native browser with exactly one `NativeCoopJoinMenu.TestHosts` entry: “Friend”, no avatar. This is the requested synthetic host-list entry; no Steam friend scan or connection is represented by it. |
| `players-and-invite.jpg` | P1's native pause menu and Players panel during the connected Neighborhood session, with both P1/P2 cards and no Steam identity. |

These are staged screenshots from a real connected session, not a natural two-person acceptance playtest. Capture controls are gated by `PBDMP_README_CAPTURE=1` and do not run in normal play. Existing campaign/Survival acceptance matrices are unchanged.

## Capture handoff — October 20, 2026

**Complete:** all seven final JPGs were reviewed after downscaling for rendering, composition, identity and fixture data. The final two-process capture used build `0FBBAAD56AA4C9CC`, the command above, Neighborhood, Prison and Suburbs. Both current loader logs confirm the same build, Direct IP connection and the upload guard; each records four blocked startup submissions. Neither current Unity log records a successful score upload. The runner finished and cleaned up its recorded processes.

All tiles are 640×360 and below 120 KB; the center is 1280×720 and below 300 KB. Only resize/compression/metadata stripping was applied to the frame pixels. Continue's smaller preview and the hidden level-select leaderboard were staged in the live UI before capture, not composited afterward.

Final validation: build succeeds with zero warnings/errors, 41 network acceptance scenarios and 8 reporting checks pass, PowerShell parsing and gallery image links/2×3 structure pass, and `git diff --check` is clean. Both isolated plugin DLL hashes match the final build. The retired PNGs have no remaining references; the player-model PNG is retained.

Practical limits: the capture run's Unity logs include native `StopCoroutine` “routine is null” messages during scene unloading and FMOD device warnings. The final rendered frames were reviewed successfully, but this session does not establish a warning-free native regression pass. An earlier startup, before the capture upload guard was added, resubmitted an existing solo leaderboard score; that run supplied none of the final seven images.
