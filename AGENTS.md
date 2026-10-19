# Agent instructions

PBDMP is a Windows/BepInEx IL2CPP mod. Use the repository checkout as the source of truth; recovered game output is external research material.

## Read on demand

- **Native research, raw ISIL addresses, empty decompiled methods, or runtime testing:** read `docs/agent-research-and-testing.md` before drawing behavioral conclusions or launching fixtures.
- **Survival work:** read `survival.md` for approved rules, native references and phase gates. Update its Pending / In progress / Complete handoff as work advances.
- **Weapon presentation:** read `animation-pass.md` and `docs/weapon-animation-testing.md` for completed evidence, limitations and focused fixtures.
- **Synchronization changes:** read `docs/protocol.md` and relevant rows of `docs/sync-progress.md`; preserve host authority, epoch/life identities, personal resources and bounded snapshots.
- **Save changes:** read `docs/co-op-saves.md`; retain solo/co-op isolation and compatible campaign files.

## Working rules

1. Inspect Git status and preserve unrelated edits, including another agent's work. Keep commits/pushes within the owner's explicit request. A plan is not authorization to implement it.
2. Locate the current installation from ignored `.local/plugin-install.json` when present; discover research inputs using `.local/research-paths.json` or their recovery manifest. Supply explicit `-GameDir` arguments; script legacy defaults are not installation discovery.
3. Build against the selected installation's real `BepInEx/core` and generated `BepInEx/interop`. RecoveredDLLs/DummyDll are analysis artifacts, not runtime references.
4. Keep Unity/IL2CPP calls on Unity's main thread. Workers receive bounded owned/immutable data. Visual replicas must not execute damage, AI or progression callbacks.
5. Native fixtures run sequentially against isolated copies, with only their own processes cleaned up. Respect a user's running game and keep normal saves/installation manifests intact. Use current-run evidence; distinguish synthetic fixtures, native callbacks and natural playtests.
6. Read/write text explicitly as UTF-8. Keep personal paths, identities, raw logs, game files and extracted assets in ignored local storage. Public docs use relative repo links and `<RepoDir>`, `<GameDir>`, `<Decompiled>` placeholders.
7. Run relevant checks, record exact evidence and practical limits, then update granular sync rows/handoffs. Reopen completed test matrices only when changed code or a reported regression justifies it.
