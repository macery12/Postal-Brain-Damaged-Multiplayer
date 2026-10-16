# Weapon and player animation pass — handoff

Updated October 16, 2026. User approved implementation, local testing and installation; **no commit or push**. Preserve all existing uncommitted megapass work. Focus is remote weapon grips/actions, starting with the saw, then supporting player poses on the existing model. Do not expand into combat authority, saves or world behavior.

## Live task status

**Current stopping point: complete and installed.** Build **C990AE9ED22FC33F**, version 0.10.0 / protocol 11 / save schema 2. All diagnostic games and runners are closed. No commit/push. The normal installation and matching partner ZIP contain the independently hash-verified DLL pair. No required work remains in this focused pass.

| Task | Status | Evidence |
| --- | --- | --- |
| Research native weapons, skeleton and clips | Complete | Source map and research caveats below. |
| Capture original holding angles | Complete | `.local/weapon-before-20261016-103100`. |
| Explicit grips, axes, aim and supporting hands | Complete | Regular, saw, bow and dual variants pass strict hand reach checks. |
| Native materials and saw teeth | Complete | Preserves tint/alpha; owned UV animation follows native active/idle state. |
| Movement/action snapshots and supporting poses | Complete | Wire 11 extension, native callbacks, grounded crouch, jump/air/slide/kick/swap and blended clips. |
| Dual-wield presentation | Complete | Base pickup aliasing, separate grips and handed native flashes. |
| Full visual/action matrix and review | Complete | `.local/weapon-release-20261016-144901`: 27 variants × 23 views/states = 621 PNGs. Strict grip/foot checks and reviewed contact sheets. |
| Connected native equips/actions/pause | Complete | `.local/travel-20261016-144937`; both-direction native equips/pistol/shotgun/saw and explicit action-state/pause tests. |
| Performance / managed checks / smoke | Complete | 34 network + 8 reporting; runtime smoke passes; 0.10426 ms mean / 0.10550 ms p95 and 0 B/1000 avatar updates. Area69 `.local/travel-20261016-145157` passes world/sweep 3.067/52.826 ms p95 and join/pause/input. |
| Sync/protocol/testing/verification docs | Complete | Granular `docs/sync-progress.md`, `docs/weapon-animation-testing.md`, protocol, verification and concise performance report updated. |
| Install and matching partner ZIP | Complete | `artifacts/PBDMP-0.10.0-animations-C990AE9ED22FC33F.zip`. Backup pointer `.local/animation-install-backup.txt`; package pointer `.local/animation-package.txt`. |

## Pending follow-ups (outside this completed pass)

| Follow-up | Status | Boundary |
| --- | --- | --- |
| Exact first-person internal mechanisms | Pending | Native pump/bolt/barrel/string deformation parity is not provided by static pickup meshes. Character clips and authored overlays provide current action presentation. |
| Full player-death animation | Pending | Existing life-state visibility remains; a separate body/death presentation task. |
| Natural alternate-mode/campaign visual variants | Pending | Native fixtures cover equips and three attack families; remaining action fixtures are synthetic. Maintainer can playtest natural modes without reopening this pass. |

Continue here only for new user requests or a reported regression. Do not redo the completed matrix or reopen the earlier megapass. See the source map below for focused lookup.

## Source map

- `src/PBDMP.Plugin/WeaponGripProfile.cs`: explicit pickup axes, neutral presentation pivots, dimensions, grip/support/hold points. Cached profiles (avoid per-frame record allocations).
- `src/PBDMP.Plugin/RemotePlayerVisual.cs`: IK arms, palm orientation/finger curl, pitch smoothing, recoil/equip offsets, crouch, material scrolling, dual geometry and muzzle sockets.
- `src/PBDMP.Plugin/RemoteAnimation.cs`: event-stripped native clips; cached names/joints; idle base + partial armed clips, blend, air/jump/slide/kick/swap; restore bind transforms before samples to prevent IK accumulation.
- `src/PBDMP.Plugin/LocalWeaponObserver.cs`: real shot and attack callbacks; movement/charge/block/shield/hand capture. Delegate cleanup on rebinding.
- `src/PBDMP.Network/PlayerAnimation.cs` and `Protocol.cs`: 15-byte presentation extension, protocol 11. Current flags include LeftHand=256 and SawRunning=512; mask 1023. This state never authorizes combat.
- `src/PBDMP.Plugin/VisualClone.cs`: retain native cloned materials, preserve inactive pickup variants, fallback textured gun material instead of hands/particles.
- `src/PBDMP.Plugin/RemoteMuzzleEffect.cs`: uses shared audited visual-source whitelist, delta-aware particle simulation; dual flashes use native base-weapon sources.
- `src/PBDMP.Plugin/NativeAssetGraph.cs`, `SharedWeaponEffects.cs`: effect-to-weapon origins and remote visual muzzle socket placement (presentation only).
- `src/PBDMP.Plugin/MultiplayerBehaviour.cs`: publishes presentation state, smooths yaw, freezes remote animation delta during shared holds, opt-in diagnostic entry points.
- `src/PBDMP.Plugin/PoseCaptureDiagnostics.cs`: 768px fixed angles, grip close-ups, +/-45 and +/-85 aim, crouch, early/late firing/melee, alternate, charge/block/shield, loop/air/slide/kick; strict grip and script/collider checks; completion microbenchmark. Waits one frame between pose and camera render for Unity skin updates.
- `scripts/Test-WeaponPresentation.ps1`: isolated capture runner; moves previous loader log aside, fails fast on runtime errors; finally stops only its own process.
- `src/PBDMP.Plugin/WeaponPresentationDiagnostics.cs` + `scripts/Test-Travel.ps1 -WeaponPresentation`: actual native inventory equips, native primary callbacks for pistol/shotgun/saw, explicit synthetic action-state transport fixtures, shared pause clock checks. Synthetic action fixtures must not be claimed as natural gameplay acceptance.
- `tests/PBDMP.Network.Tests/Program.cs`: presentation codec/malformed-state test; existing carrier byte mutation offset adjusted by appended 15 bytes.

## Repeatable commands

Run from repository root. Use the private game copy; never run installation against it (normal install manifest belongs to the actual Steam installation).

```powershell
./scripts/Build.ps1 -GameDir ./.local/acceptance-host -SkipTests
Copy-Item ./artifacts/PBDMP/BepInEx/plugins/PBDMP/PBDMP.dll, ./artifacts/PBDMP/BepInEx/plugins/PBDMP/PBDMP.Network.dll ./.local/acceptance-host/BepInEx/plugins/PBDMP -Force
./scripts/Test-WeaponPresentation.ps1 -GameDir ./.local/acceptance-host -Label final
./scripts/Test-Travel.ps1 -GameDir ./.local/acceptance-host -WeaponPresentation -Level LEVEL_AD_Neighborhood
```

Run native fixtures sequentially; they share private host/peer copies. Full final build omits `-SkipTests`. Then run the existing runtime smoke and Area69 performance budget once final concerns justify them. `Test-Runtime.ps1` shotgun aim check now waits for the intentional pitch interpolation before asserting alignment.

## Research and test caveats

- The old code aligned the longest bounding-box axis, centered the mesh at the wrist and ignored a true grip. Saw pickup has an authored display tilt; normalize its `Shovel` node then rotate 90 degrees about X. Rocket pickup faces backwards; explicit yaw 180. Bow stays vertical/left-handed.
- Saw native `AnimateShovelTeeth` uses owned material offset: damage window -100 * delta, idle +1 * delta. A dedicated SawRunning presentation flag now follows native `_isDamageEnabled`; this never authorizes combat.
- `PlayerWeapon.ForceAttack()` is a native no-op stub. Do not use it as proof of real firing. `PlayAttackAnimation(ANIM_SHOOT)` exercised pistol/shotgun callbacks, but saw needs its own press/release flow.
- `VisualClone` still uses pickup geometry. Native third-person character clips exist for machinegun, shotgun and rocket families plus movement/action clips; no dedicated third-person saw set. Weapon internal mechanisms beyond teeth/materials and pose offsets may need further work; do not claim every first-person mechanism has been cloned.
- Initial one-frame render mismatch and intermediate crouch artifacts are superseded diagnostics, not final screenshots. Final release images were reviewed after joint-reset/base-layer and grounded-foot corrections.
- Original `Test-WeaponPresentation` briefly could read a previous log's PASS; now moves previous log before launch and requires current-run inventory output. The baseline 103100 has actual images. Assets-only 103305 is invalid; corrected native asset dump is `.local/weapon-assets-20261016-103318/weapon-assets.txt`.
- `.local/weapon-actions3-20261016-104952` passed old non-dual matrix, but predates final partial-layer reset/dual support. Do not call it final acceptance.
- Base category excludes some installed extra weapons. Capture fixture intentionally includes available extras and dual variants as robustness; only base campaign is promised.
- 20 Hz pose loop unchanged: extra 15 bytes gives about 300 bytes/s per sending peer before transport overhead. No world/bone transform stream added.
- Normal saves were untouched. Normal DLLs are now the tested animation build C990AE9ED22FC33F; old megapass pair is backed up. Share the matching animation ZIP with the partner.
