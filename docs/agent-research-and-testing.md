# Reading native game output and testing PBDMP

Use this guide for game-behavior research, address resolution and native regressions. Paths use the README's `<RepoDir>`, `<GameDir>` and `<Decompiled>` placeholders. Keep generated indexes, logs and local path configuration in ignored `.local/` or the external recovery tree.

## Locate and verify the inputs

Check Git status before editing. `.local/plugin-install.json` identifies the current normal installation when available; it may differ from the installation used for decompilation. Optional `.local/research-paths.json` records this checkout's external recovery root. Keep both path records private.

Read `<Decompiled>/README.md` and `manifest.json` for tool provenance, Unity/metadata version and input SHA-256 hashes. When applying research to another installation/build, compare its `GameAssembly.dll` and metadata hashes. Recheck/recover mismatched inputs rather than transplanting offsets. The repository excludes game/recovery/interop files; missing recovery is an environment prerequisite, not evidence that a method is absent.

## Evidence hierarchy

| Input | Use | Limit |
| --- | --- | --- |
| `<Decompiled>/CSharp/<assembly>/.../<Type>.cs` | Names, inheritance, signatures, enum values, metadata tokens, field offsets, native RVA/offset/length attributes. | `throw null`, default returns and empty bodies are usually generated placeholders, not native behavior. |
| `<Decompiled>/Cpp2IL/Native-ISIL/IsilDump/<assembly>/.../<Type>.txt` | Native disassembly and intermediate instructions grouped by `Method:`. | Follow branches, tail calls, coroutines and callees; a flattened call list is not the control flow. Recovery may be incomplete. |
| `<Decompiled>/Il2CppDumper/script.json` | Method address-to-name/signature mapping, including shared/generic aliases. | This recovery stores method addresses as decimal RVAs. Verify the convention against an Address attribute and PE header in another dump. |
| `<Decompiled>/Il2CppDumper/dump.cs`, `il2cpp.h`, `stringliteral.json` | Additional declarations, structures and string literals when needed. | Large files: search narrowly. Static/type storage is not an instance field. |
| `<GameDir>/BepInEx/interop` and `BepInEx/core` | Actual generated wrappers/loader references for plugin builds. | These do not establish native behavior; dummy/recovered DLLs cannot substitute for them. |
| Runtime fixtures and natural gameplay | Initialization, scene bindings, callbacks, pooling and visible results. | Direct callback invocation or synthetic state changes establish only the exercised path. |

Start with `rg --files` / `rg -n` in the relevant namespace/assembly. Match the C# declaration's path to the same namespace/type under IsilDump. Generated nested files may contain `_NestedType_`; coroutine behavior often lives in their `MoveNext`, not the iterator factory.

## Resolving a direct call

For a disassembly virtual address (VA): **RVA = VA minus preferred PE ImageBase**. File offset is different; use PE section mapping for raw file bytes. A running process may relocate the module under ASLR, so runtime addresses require the actual loaded base.

Verify ImageBase from the matching binary's PE header, using an installed PE inspector or Python's `struct`. For x64 PE32+, read DOS `e_lfanew` at file offset `0x3c`, validate the PE signature/machine and optional-header magic, then read the 64-bit ImageBase at `PE header offset + 24 + 24`. Do not guess the base from the leading address digits.

Worked example checked against the actual reference PE and map:

| Operation | Value |
| --- | --- |
| `HordeModeController.Begin` direct call in disassembly | `0x180E6C3D0` |
| Preferred ImageBase read from matching PE | `0x180000000` |
| RVA | `0xE6C3D0` = decimal `15123408` |
| Exact ScriptMethod match | `Hyperstrange.PBD.HordeModeController$$CombatStart` |
| Same call in recovered ISIL | `Call HordeModeController.CombatStart` |
| Begin's tail call | `0x180888150` → RVA `0x888150` = `8946000` → `HordeModePickupSpawner$$SpawnAll` |

Search an exact decimal address without repeatedly parsing the whole large JSON:

```powershell
$Decompiled = Read-Host 'Recovery root containing CSharp, Cpp2IL and Il2CppDumper'
$ImageBase = [Convert]::ToUInt64('180000000', 16) # replace if the verified PE differs
$CallVA = [Convert]::ToUInt64('180E6C3D0', 16)
$rva = $CallVA - $ImageBase
rg -n -F ('"Address": ' + $rva + ',') (Join-Path $Decompiled 'Il2CppDumper/script.json') -A 3
```

Inspect Name/Signature and confirm the declaration's RVA. Multiple names may share one address due to generic sharing, folded code or common thunks: retain all candidates, then use receiver type, signature and caller context. For repeated queries, build/reuse a private index. An exact missing match does not justify choosing the nearest method name; first determine whether the target is an interior block or runtime helper.

`Call 0` can represent an **indirect call**, not a call to address zero. Read the original `call qword ptr [...]` and trace its register/delegate/virtual target. Some helpers/targets have no recovered symbol; label these unknown. Use native decompilation if available and warranted. Supplied IDA/Ghidra import scripts do not imply those tools have been installed or run.

## Trace behavior before patching

1. Identify the exact type/overload, FieldOffset attributes, native address and matching Method section.
2. Trace relevant branches, tail jumps, delegate subscribers and coroutine MoveNext. Account for the proposed hook's prerequisites/downstream effects, or explicitly record unknowns.
3. Resolve important callees and read their bodies. Windows x64 instance methods commonly receive `this` in RCX; interpret registers/float argument slots using the actual signature. ISIL's generic argument list is not a recovered high-level signature. Match offsets to the correct receiver type; ISIL often uses decimal while declarations use hexadecimal.
4. Inspect initialization, disable/reset/pooling and actual authored scene bindings. A manually built object does not establish normal lifecycle safety.
5. Record declaration facts, native behavior, inference and runtime evidence separately with source paths/methods and the matching build. Completion means a reviewer can reproduce the lookup.

A C# placeholder is not a native no-op. Actual native `ret` can establish a no-op for that binary. `PlayerWeapon.ForceAttack()` was a native no-op here; pistol/shotgun fixtures used `PlayAttackAnimation(ANIM_SHOOT)`, whereas the saw needed native press/release flow.

Survival warning: `SetHordeModeState` has gameplay side effects; `Begin` starts combat and pickup spawning. Calling these on P2 merely to update its HUD can create duplicate authority. `docs/plans/survival.md` contains the approved rules and additional source references.

## Focused testing workflow

Read the selected runner's `param` block and native fixture implementation before using switches. Requirements live in code; some fixtures need particular authored components. **`Test-Travel.ps1 -Arena` tests campaign arena-shift mechanics, not Survival/Horde.** Discover real level IDs; do not guess scene names or invent switches.

Build against an isolated installation with compatible loader-generated interop:

```powershell
# From <RepoDir>; enter a private test copy, not the normal installation.
$TestGameDir = Read-Host 'Isolated test game folder'
./scripts/Build.ps1 -GameDir $TestGameDir
# Build produces artifacts; it does not install them into that game copy.
$target = Join-Path $TestGameDir 'BepInEx/plugins/PBDMP'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item ./artifacts/PBDMP/BepInEx/plugins/PBDMP/PBDMP.dll,
          ./artifacts/PBDMP/BepInEx/plugins/PBDMP/PBDMP.Network.dll -Destination $target -Force
./scripts/Test-Runtime.ps1 -GameDir $TestGameDir
./scripts/Test-Travel.ps1 -GameDir $TestGameDir -JoinInput -Level LEVEL_AD_Neighborhood
```

- `Build.ps1` runs managed network/reporting checks unless `-SkipTests` is selected; native fixtures are separate. Use matching plugin/network DLLs on both processes and record exact build/pair hashes.
- Copy DLLs directly into private fixtures. `Install-Plugin.ps1` owns the normal installation manifest/backups; pointing it at a fixture redirects that state. Inspect the manifest before normal installation.
- Run native fixtures sequentially. `Test-Travel` reuses ignored `.local/travel-client` with some junctions/hard links. Treat linked content as shared/read-only: writes/deletes through links can affect the source. Verify resolved targets before recursive cleanup.
- Runners refuse an existing game; wait for the player to close it. Stop only recorded test PIDs, and restore temporary environment variables in cleanup.
- Require current-run PASS/output, then inspect both loader logs and Unity/teardown errors. Old PASS lines/screenshots or a surviving process are insufficient. Preserve prior logs before reruns as needed; raw evidence stays ignored.
- Hidden-window launch is not headless execution and does not guarantee mouse/focus isolation. Preserve the diagnostic cursor guard. Focus work remains separate scope unless needed for the bug.
- Steam initialization failure is a launch prerequisite failure. Diagnose current logs without weakening assertions to manufacture a sync pass.

Use a focused failing native reproduction, apply the fix, then retain its assertion. Expand testing when changed seams/new failures warrant it. Direct native invocation and synthetic transport fixtures are useful but cannot certify every natural gameplay route. `docs/verification.md` separates recorded evidence from remaining maintainer checks.

For performance comparisons, keep arena/workload/caps consistent; distinguish one-PC/two-process contention from world/network work. Record frame percentiles/worst stalls, allocation and measured directional traffic using current diagnostics.

## Completion and handoff

Record source/build identity, exact command/map/fixture, current-run result and practical limits. Update relevant plan and granular sync rows with accurate Pending/In progress/Complete states. Public references use placeholders and method names; keep raw native dumps/assets, local paths and unsanitized logs private. Commit/push only when requested.
