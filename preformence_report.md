# Performance report

| Test / measurement | Before | After |
| --- | ---: | ---: |
| P1 FPS — one game offline | 120.0 | 120.0 |
| P1 FPS — two games offline | 120.0 | 120.0 |
| P1 FPS — hosting without P2 | 102.4 | 119.9 |
| Connected FPS — P1 / P2 | 71.3 / 95.6 | 117.9 / 118.9 |
| Frame p99 — P1 / P2 (ms) | 95.2 / 66.1 | 18.0 / 9.0 |
| Longest frame — P1 / P2 (ms) | 124.2 / 81.8 | 25.8 / 50.6 |
| Frames over 50 ms — P1 / P2 | 72 / 49 | 0 / 1 |
| Frames over 100 ms — P1 / P2 | 14 / 0 | 0 / 0 |
| Host sync work — p95 / max (ms) | 3.49 / 108.19 | 3.10 / 15.12 |
| Main-thread allocation — P1 / P2 (MiB/s) | 49.4 / 30.9 | 22.3 / 2.3 |
| Rotator lookup allocation — sampled over 20 s | ~442 MiB | ~0.4 MiB |
| Total managed allocation — sampled over 20 s | ~836 MiB | ~454 MiB |
| Traffic — P1 → P2 (KiB/s) | 65.8 | 81.0 |
| Traffic — P2 → P1 (KiB/s) | 4.3 | 4.3 |
| Area69 host world / sweep p95 (ms)* | 3.45 / 203.29 | 3.08 / 47.06 |
| Native ownership / urine / late join / pause-input checks | Existing checks | Pass |
| Managed network + reporting checks | 39 pass | 40 pass |

Neighborhood idle tests: 25-second samples, eight-second warmup, frozen NPC AI, 1280×720, 120 FPS cap; both games on one PC. Before: `7106890D0DCAD31A`; after: `8C014EA539801E69`. Allocation/traffic comparisons include separate matching idle runs.

Traffic measures framed application bytes, excluding TCP/IP, Steam overhead and retransmissions; more traffic can reflect faster state delivery. Profiler allocation totals are estimates. *Area69 before is the earlier recorded baseline. Active-combat and broader campaign performance remain separate work.
