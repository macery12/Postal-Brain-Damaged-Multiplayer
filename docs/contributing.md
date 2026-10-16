# Contributing

PBDMP is an experimental Windows/BepInEx IL2CPP mod. Read the [sync tracker](sync-progress.md), [protocol](protocol.md), [verification](verification.md) and [roadmap](roadmap.md) before extending it.

## Build and changes

Follow the root [build instructions](../README.md#build-and-install), supplying your own game path with `-GameDir`. Use .NET SDK 10 and compatible BepInEx-generated interop. A source checkout does not include the game, loader or recovered assemblies.

Keep all Unity/native calls on Unity's main thread. Network workers exchange bounded immutable/owned data rather than Unity objects. Preserve host authority, epoch/life identity, personal resource rules, save isolation and frame-budgeted state capture. Replica presentation must not apply damage or replay progression callbacks.

Investigate native behavior before hooking it. Recovered C# bodies can be placeholders, and short/shared IL2CPP method thunks may be unsafe detour targets. Prefer narrow typed adapters and a reproducible native regression for behavior changes. Run relevant network/native checks and record remaining gaps; a catalogue census is not proof of natural campaign completion.

The optional `scripts/Audit-VisualParticles.py` research tool requires Python and UnityPy (the reference audit used 1.25.4). Run it with `--game-dir` pointing to your own game installation. It regenerates `AuditedVisualSources.cs` and writes an ignored audit report; it is not required to build or play.

To reproduce the current allowlist, retain all previously audited files when adding a source. For the reference base-campaign build, this includes the cannon, COVID arena and train data:

```powershell
python ./scripts/Audit-VisualParticles.py --game-dir $GameDir `
  --additional-asset sharedassets5.assets --additional-asset level5 `
  --additional-asset sharedassets10.assets --additional-asset level10 `
  --additional-asset sharedassets15.assets --additional-asset level15
```

The shared base asset file is included automatically. Review the generated registry diff and investigate removed names rather than silently discarding a prior audit. Match scene files to your installed build; do not distribute recovered asset data.

## Documentation and privacy

Use relative repository links and the README's `<GameDir>`, `<RepoDir>` and `<Decompiled>` placeholders. Do not put a developer's username, drive layout, timezone, personal email or network configuration into instructions. Public Git identity is the contributor's choice; inspect author/committer metadata before publishing if privacy matters to you.

Do not commit `.local`, `artifacts`, `bin`, `obj`, game/loader binaries, generated interop, saves, installation manifests/backups, private configuration, credentials or raw logs. Rendered examples should have no personal overlays or embedded identifying metadata; game imagery remains subject to its owners' rights.

Before staging, inspect the exact candidate files for secrets, absolute workstation paths and generated content. Ignore rules do not protect files already tracked. Review the staged diff and commit identity before committing. Preserve the game's and third-party projects' licensing; the repository's MIT license covers original PBDMP source/documentation.

## Reports and verification

Include the mod version/protocol, map, host/client role, fresh/reload/late-join sequence, expected result and actual result. Attach only sanitized log excerpts. State whether the check used synthetic/native diagnostics or natural two-PC gameplay. Do not mark a planned/partial feature complete without the relevant implementation and evidence.


### In-game bug reports

The main-menu **Report a Bug** button opens the [Bug or sync issue form](../.github/ISSUE_TEMPLATE/bug-report.yml). Only a title and problem description are required. Software versions/build identifiers and available last-multiplayer-session context are prefilled; users can review and edit them. Hardware specifications, usernames, file paths, Steam IDs, IP addresses and raw logs are excluded. Screenshots and sanitized logs are attached manually by the reporter.

Last-session context is held only in memory for the current game launch. It records the most recently played supported campaign map, host/joining role and Steam/Direct IP connection type, and survives returning to the title or disconnecting. Starting another session clears the previous session's map until the new map is ready. Reports from a fresh launch use unavailable values. The game compatibility identifier is a binary hash, not a Steam release/build number.

Maintainers must publish `.github/ISSUE_TEMPLATE/bug-report.yml` on the repository's default branch before the button's form link works. Keep the `software` and `session` field IDs aligned with `ReportDraft.cs`; no GitHub token or API integration is used. The template, URL and cache have focused managed checks included in `Build.ps1`; `Test-News.ps1` checks native menu actions/software prefill, while `Test-Travel.ps1` checks actual session context through travel and disconnect.
