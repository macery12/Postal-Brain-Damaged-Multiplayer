# Campaign component coverage

> Historical research/census. Statements about scope, missing features and implementation status describe the dated milestone below. For current 0.7.0 behavior and limits, use [sync progress](sync-progress.md) and [verification](verification.md). Asset identifiers describe the reference build and are not portable runtime constants.

October 12, 2026. All 15 shipped base-campaign maps loaded and their included component identities and bounded door/encounter state adapters were inspected in actual IL2CPP processes. This is an adapter census, not a complete co-op playthrough or proof of every interaction.

| Map | Included components | Door motion transforms | Encounter motion transforms | Native families found |
| --- | ---: | ---: | ---: | --- |
| LEVEL_AD_Neighborhood | 443 | 475 | 0 | Door: 117, Mover: 1, NpcGroupWipeListener: 6, SpawnPoint: 315, SpawnPointsDeathListener: 1, Toggler: 3 |
| LEVEL_AD_Prison | 389 | 340 | 0 | Door: 26, DoubleDoor: 10, Mover: 188, MoverController: 1, SpawnPoint: 164 |
| LEVEL_AD_TheWall_Interior | 162 | 69 | 0 | Door: 7, DoubleDoor: 3, ElevatorDoors: 1, Mover: 7, Rotater: 3, SpawnPoint: 141 |
| LEVEL_AD_TheWall | 205 | 8 | 0 | SpawnPoint: 204, Toggler: 1 |
| LEVEL_AD_Wallmarkt | 102 | 19 | 8 | DoubleDoor: 1, SlidingDoors: 2, SpawnPoint: 97, SpawnPointDeathListener: 1, Toggler: 1 |
| LEVEL_Asylum_Area69 | 285 | 106 | 0 | Door: 11, DoubleDoor: 5, Mover: 4, SlidingDoors: 3, SpawnPoint: 252, ToiletDoor: 10 |
| LEVEL_Asylum_Backyard | 271 | 87 | 0 | Door: 27, Mover: 3, Rotater: 2, SpawnPoint: 238, Toggler: 1 |
| LEVEL_Asylum_CoronvirusDen | 6 | 3 | 1 | Mover: 2, MoverController: 1, SpawnPoint: 1, SpawnPointDeathListener: 1, Toggler: 1 |
| LEVEL_Asylum_Forest | 310 | 17 | 0 | Door: 1, DoubleDoor: 1, Mover: 5, SpawnPoint: 299, SpawnPointsDeathListener: 1, Toggler: 3 |
| LEVEL_Asylum_Hospital | 337 | 111 | 0 | Door: 24, DoubleDoor: 3, Mover: 2, NpcGroupWipeListener: 4, Rotater: 3, SpawnPoint: 301 |
| LEVEL_NLM_ComicCon | 311 | 100 | 0 | Door: 5, DoubleDoor: 3, Mover: 13, SlidingDoors: 5, SpawnPoint: 273, ToiletDoor: 12 |
| LEVEL_NLM_DeathStar | 211 | 143 | 0 | Mover: 3, SlidingDoors: 31, SpawnPoint: 177 |
| LEVEL_NLM_ElonDuskChambers | 8 | 5 | 36 | Hookable: 1, JumpPad: 3, LastBossArena: 1, SlidingDoors: 1, SpawnPoint: 2 |
| LEVEL_NLM_SozebTrain | 241 | 219 | 0 | Door: 3, DoubleDoor: 1, Mover: 20, Rotater: 4, SlidingDoors: 29, SpawnPoint: 182, Toggler: 2 |
| LEVEL_NLM_VIPZone | 247 | 90 | 0 | Door: 5, Mover: 14, Rotater: 11, SlidingDoors: 7, SpawnPoint: 206, Toggler: 1, ToiletDoor: 2, Vault: 1 |

Scene paths and actual scene-root ordering resolve duplicate root names; hierarchy/component indices identify authored objects. Generated native save/spawn GUIDs remain excluded as cross-PC identities. A manifest mismatch ends the session instead of guessing a match.

At the time of this census, moving elevator platforms, TNT/destructible gates, the damaging cat-paw trap and unrelated machinery were excluded. The 0.7 mission pass adds partial coverage for referenced machinery; see the current sync tracker. Generic movers are included only through door/gate ancestry or direct combat/door dependencies. These names need authored-map review where an apparent gate has a different name or mechanism.

The repeated cross-episode audit exposed a native NPC pool initialization error after ComicCon; VIP Zone passed in a fresh process. The remaining maps were audited separately. Ordinary campaign next-level and disconnect regression must be reported independently from this census.

Raw reports are not distributed. Run `scripts/Test-Travel.ps1 -GameDir $GameDir -Audit -AuditLevels LEVEL_Name` from the checkout to inspect one map with profile writes blocked, or use `scripts/Test-ActorCampaign.ps1 -GameDir $GameDir` for isolated campaign audits.
