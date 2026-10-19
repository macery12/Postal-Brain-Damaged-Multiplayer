# Multiplayer performance

## Initial measured baseline

Measured on October 16, 2026, before performance fixes, with diagnostic build `7106890D0DCAD31A` on `LEVEL_AD_Neighborhood`.

Each phase has an eight-second warmup followed by approximately 25 seconds of frame sampling. Both game instances run on the same PC at 1280 × 720, with VSync disabled and a 120 FPS limit. Players remain stationary and NPC AI is frozen in every phase to isolate idle multiplayer overhead. The mod remains loaded when disconnected. These measurements compare connection states, rather than an unmodded game against a modded game. They do not establish active-combat performance or performance on separate PCs. FPS values near 120 reach the configured limit and do not measure the uncapped ceiling.

| P1 state | Mean FPS | Frame p95 | Frame p99 | Longest frame | Frames >50 ms | Frames >100 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| One game, offline | 119.96 | 9.08 ms | 9.19 ms | 13.07 ms | 0 | 0 |
| Two games, offline | 119.98 | 9.12 ms | 9.24 ms | 9.89 ms | 0 | 0 |
| Hosting without P2 connected | 102.45 | 13.99 ms | 37.32 ms | 53.32 ms | 1 | 0 |
| P2 connected | 71.28 | 17.80 ms | 95.24 ms | 124.19 ms | 72 | 14 |

| P2 state | Mean FPS | Frame p95 | Frame p99 | Longest frame | Frames >50 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Offline, while P1 is offline | 119.98 | 9.10 ms | 9.25 ms | 12.16 ms | 0 |
| Connected | 95.65 | 14.56 ms | 66.09 ms | 81.80 ms | 49 |

Neither connected sample contains a frame over 250 ms. The regression is still substantial: P1 retains only 59.4% of its two-process offline FPS, while P2 retains 79.7%. The two disconnected instances both reach the FPS limit, so the observed connected slowdown cannot be attributed solely to having two idle game processes open.

The connected host's shared-world work is 3.49 ms at p95, 8.78 ms at p99 and 108.19 ms at maximum. Typical world work fits the existing 4 ms target, while individual operations can exceed a frame budget severely. Whole-frame stalls also occur outside the measured mod-update region. Attribution still requires CPU/allocation profiling; a passing p95 alone does not establish smooth gameplay.

## First hardening result

The [brief before/after report](../preformence_report.md) records the final measurements. The fix caches authored urine-surface membership per map rather than rebuilding scene-root paths and hashes during every native rotation update. Authored scripted actors and ordered motion targets are also cached when the catalogue is built. Host authority, capture scheduling, motion ordering and catalogue compatibility hashes stay the same. Native checks compare cached membership, actor identities and motion ordering against the original queries on both games.

The final untraced Neighborhood sample reaches 117.9 FPS on P1 and 118.9 FPS on P2. P1 has no frames above 50 ms; P2 has one 50.6 ms frame. Both pass the fixed retained-FPS/frame-p99/stall criteria, and host world p95/max remain within 4/16 ms in this sample. A preceding fixed run still had rare large outliers, so these results do not establish the absence of intermittent stalls. The profiler estimates the targeted lookup at 442.2 → 0.4 MiB over separate 20-second traces, with total sampled managed allocation 835.7 → 454.2 MiB. The remaining allocation includes world-frame creation, physical-state serialization and enemy presentation.

Application traffic is measured directly from completed protocol frames, including their four-byte length prefix, only when the benchmark is enabled. The counter test verifies completed writes/reads and excludes failed reads. Idle P1 outgoing traffic rises from about 65.8 to 81.0 KiB/s, while P2 outgoing traffic stays around 4.3 KiB/s. This pass reduces CPU/allocation costs; it does not claim a bandwidth reduction. These measurements exclude IP/TCP/Steam overhead and retransmissions.

The subsequent native Area69 check passes at a 120 FPS cap: host world p95 3.077 ms and sweep p95 47.058 ms under the unchanged 4/200 ms targets, plus join and pause/input assertions. Urine/machinery, toilet overflow/flush, fireball extinguishing and late-join presentation pass. The urine fixture now drives native tutorial Use and captures after both games reach a settled shared pause; tolerances were not relaxed. Those fixture-only changes follow the measured timing build.

## Reproducing the comparison

Close running game instances first. Use a generated-BepInEx game directory containing the exact diagnostic build for both players. The runner owns its test processes and stops only those processes.

```powershell
./scripts/Test-Travel.ps1 -GameDir $GameDir -PerformanceBaseline -Level LEVEL_AD_Neighborhood
./scripts/Test-PerformanceBudget.ps1 -ResultsDir $ResultsDir
```

The runner prints its private results directory. It contains separate host/client summaries, section timing and per-frame TSV files. `single-offline`, `dual-offline`, `host-no-peer` and `connected` identify the phases. P2 returns to the title screen after the offline samples before joining P1. Sampling starts only after both native gameplay input and the current-world join barrier are released. No log or TSV file is written per measured frame; samples are buffered and exported between phases. The first baseline’s GC counters include warmup; later samples reset them when frame sampling begins. Use frame timing for the baseline table.

The budget check retains fixed targets: each connected player must retain at least 90% of their two-process offline FPS, keep frame p99 at or below 33.333 ms and have at most one frame over 50 ms per sample. Host shared-world work must remain at or below 4 ms p95 and 16 ms maximum. The initial baseline fails both player checks and the host maximum-work check. These thresholds detect this reproduced regression; passing them does not close broad campaign performance acceptance.

The older `-Performance` test separately checks host world p95 and complete sweep latency. Its 200 ms sweep target remains unchanged.

## Survival active-combat comparison (October 18, 2026)

`Test-Travel.ps1 -SurvivalSoak` on Suburbs runs the same active workload three ways: native wave 1 with enemies spawning naturally and the fixture killing every live enemy each 1.5 s. Both processes share one PC at a 120 FPS cap, 1280x720; 8 s warm-up, 25 s sample. This is not a two-PC network measurement.

| Configuration | Mean FPS | p50 / p95 / p99 ms | Worst ms | Frames >50 ms | Main-thread alloc MiB/s | Traffic out / in KiB/s |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| Offline native (P2 idle at menu) | 119.8 | 8.34 / 9.42 / 9.66 | 18.7 | 0 | 2.55 | — |
| P1 hosting alone | 119.7 | 8.33 / 9.44 / 9.73 | 19.5 | 0 | 2.72 | — |
| P1 with P2 connected | 119.4 | 8.34 / 9.51 / 9.89 | 21.7 | 0 | 3.77 | 55.2 / 11.9 |
| P2 connected | 119.6 | 8.34 / 9.59 / 9.99 | 25.5 | 0 | 3.32 | 11.8 / 54.9 |

Build 41D7421713392F3B. P1's connected world update: p95 0.77 ms, max 2.5 ms (campaign budgets 4 / 16 ms). The fixture then plays 15 short native waves with 321 pooled respawns. Neither PC had a frame over 100 ms (worst 30 ms on P1, 40 ms on P2), and the managed heap grew 2.0 / 2.4 MiB. Every managed ledger stays bounded by P1's live enemy pool. A one-per-frame kill burst sends at most one Survival state frame per 0.1 s sweep. Raw rows are in the run's ignored `host-soak.tsv` / `client-soak.tsv`.

## Profiling and remaining work

Use untraced runs for before/after FPS comparisons. Short, private CPU/allocation traces can identify work responsible for outliers; tracing adds overhead, so traced FPS is not interchangeable with the baseline. Microsoft's [dotnet-trace documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace) describes the CPU-sampling and GC-allocation profiles.

- Reduce remaining world-frame, physical-state and enemy-presentation allocations; investigate residual rare outliers with the same fixed budgets.
- Extend the passing urine/machinery and join/input checks to active combat and persistent-world restoration workloads.
- Extend measurements to active combat, moving machinery, save capture/restore, late-join snapshots and Steam transport.
- Measure bandwidth, transport queues and update age before claiming network performance acceptance.
- Investigate the separate already-loaded-map Prison join failure observed during harness calibration. The completed baseline uses the successfully validated main-menu join flow.

This first performance fix is validated in controlled idle scenarios. Broader active-world, combat and transport performance acceptance remains open; the sync score is unchanged.
