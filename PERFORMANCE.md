# PowerSuit Performance Certification

This document records repeatable, automated performance evidence for the current tech-demo baseline. It is hardware-specific evidence, not a promise that every target machine will produce the same results.

## Harness

`PowerSuitPerformanceSoakRunner` is compiled only in the Unity Editor and Development Builds. It is dormant during ordinary play and starts only when the player is launched with `-powersuit-soak`.

The runner:

- initializes the canonical combat sandbox and raises the SpawnDirector to the requested stress population;
- prewarms the additional enemy and shared-projectile overlap required by that stress population without changing normal encounter warmup;
- repeatedly exercises rocket, lightning, void, enemy death, replacement, projectiles, and pooled effects;
- records bounded frame-time, CPU-frame, main-thread, GC, enemy, projectile, and pool statistics without allocating while adding samples;
- writes a JSON report after measurement and exits with code `0` on pass or `2` on a failed gate when `-powersuit-soak-exit` is supplied.

Example:

```powershell
& ".\Builds\Windows\PowerSuit.exe" `
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720 `
  -powersuit-soak `
  -powersuit-soak-duration 30 `
  -powersuit-soak-warmup 10 `
  -powersuit-soak-enemies 32 `
  -powersuit-soak-fps 60 `
  -powersuit-soak-output ".\Temp\PerformanceCertification\fps60.json" `
  -powersuit-soak-exit
```

Supported bounds are 10–3600 measured seconds, 1–120 warmup seconds, 1–128 enemies, and a 30–240 FPS target. Warmup must be shorter than the measured duration.

## Certified run — 2026-08-11

Environment: Unity 6000.5.7f1 Windows x64 Development Player, Direct3D 12, NVIDIA GeForce RTX 3080, 1280×720 windowed. Each frame-rate run used a 10-second warmup, 30 measured seconds, and 32 concurrent enemies.

| Target | Samples | Frame avg / p95 / p99 / max | CPU p95 | Main-thread managed allocation p95 / max | Pool spawns / runtime misses | Logged errors |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| 30 FPS | 901 | 33.335 / 33.335 / 33.406 / 33.507 ms | 33.360 ms | 0 / 0 B | 385 / 0 | 0 |
| 60 FPS | 1,800 | 16.669 / 16.669 / 16.734 / 18.273 ms | 16.692 ms | 0 / 0 B | 492 / 0 | 0 |
| 120 FPS | 3,601 | 8.334 / 8.335 / 8.337 / 8.534 ms | 8.354 ms | 0 / 0 B | 482 / 0 | 0 |

All three runs passed their frame-budget, allocation, population, logging, fixed-buffer, and post-warmup pool-instantiation gates.

The longer lifecycle run used a 12-second warmup, 120 measured seconds, a 60 FPS target, and 48 concurrent enemies:

- 7,200 measured frames; frame average/p95/p99/max 16.668/16.669/16.712/17.505 ms;
- CPU-frame p95 16.697 ms;
- main-thread managed allocation p95/max 0/0 B;
- 175 enemies spawned, 2,455 pool spawns, 0 runtime pool misses;
- peak 79 active pooled objects and 29 active pooled projectiles;
- 0 logged errors and no captured exception, assertion, missing-reference, null-reference, or index-range pattern.

Unity's global `GC Allocated In Frame` counter did report background-thread activity (p95 1,280–2,096 B in the short matrix and 1,920 B in the long run). The runner's same-thread managed-allocation measurement was 0 B at p95 and maximum across every certified run. These are deliberately reported separately rather than treating the all-thread value as gameplay main-thread churn.

## What remains open

- The Direct3D 12 Development Player did not expose a usable draw-call recorder, and `FrameTimingManager` returned zero GPU durations. A connected Unity Profiler/Frame Debugger or graphics capture is still required for detailed render-thread, draw-call, and GPU analysis.
- The capped 30/60/120 results establish stable pacing at those targets, not uncapped maximum throughput.
- The automated lifecycle exercises abilities, enemy/projectile replacement, and pool reuse. Scene reload, player respawn, seed reset, spawner toggles, and malformed console-command soak coverage remain separate gates.
- Representative target hardware beyond the certification machine still needs measurement before setting production minimum/recommended specifications.
