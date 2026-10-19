# Issue #12 / #13 validation — 1.0.3.9 candidate

Date: 2026-10-19. Baseline source: `8b9efcb` (1.0.3.8).

This records the 1.0.3.9 comparison. The current 1.0.3.10 suite additionally executes
the production character controller and has 25 cases including male default-off behavior.

Reports: [BP state closes after a delay (#12)](https://github.com/DevoLutioner/SomaDynamics/issues/12),
[periodic FPS drops with automatic application / drift correction (#13)](https://github.com/DevoLutioner/SomaDynamics/issues/13).

## Reproduced defects and changes

`RuntimeIntegration.Tests` compiles the actual `ThighFleshJiggle`, Spring/Chain solvers,
state resets, safety checks and parameter model. Only the Unity/configuration boundary is simulated.

| Scenario | Baseline | Candidate |
| --- | --- | --- |
| Unsupported skeleton, 1,800 frames / simulated 30 s | 60 rebuilds, 1,200 hierarchy scans | 3 retries, 9 hierarchy scans; no continuing scans |
| 20 clear/reset pairs on an ordinary skeleton | 480 hierarchy scans | 0 hierarchy scans |
| External DynamicBone owns a shared thigh | Spring/Chain overwrite its rotation; clear/reset overwrite its pose | Affected side yields, including clear/reset and shape rebase |
| Shared left side, ordinary right side | Left state is lost on clear | Left preserved; right retains physics |
| Shared left side, unshared right side arrives later | Right side is not discovered | Right side is discovered by a bounded retry |
| Ordinary Spring and Chain | Motion present | Motion and clear preserved |
| Delayed body, then explicit reset | Recovers | Recovers after retry exhaustion |
| Inactive body, reactivation, then inactive again | Continues discovery indefinitely | Bounded retry budget resets after recovery |
| Unrelated particles / declared excluded subtree | Ordinary thigh works | Ordinary thigh still works |

The same 13 cases fail in seven places against baseline production source and pass against
the candidate. The failing tests exercise ownership and repeated-discovery patterns;
they do not reproduce the reporter's visual scene or establish its FPS.

Ownership uses the installed KK `DynamicBone` contract (`m_Particles`, particle `m_Transform`).
Before particle setup, declared `m_Root` and `m_Exclusions` provide a conservative fallback.
An affected side is yielded as a whole because Chain RC can rotate neighboring parents.
The external component's collider list, weight and enabled state are not changed.
Ownership is rediscovered on character reload; a temporarily disabled external chain keeps
its reservation so enabling it later does not inherit a Soma-modified rest pose.

The missing-bone path previously performed full discovery every 0.5 seconds without a limit.
Each bone lookup also allocated renderer/bone arrays again. Discovery is now indexed per
component, ordinary resets reuse that index, and background retries stop after three attempts.
A later explicit reset or character reload can discover a body that arrived after that window.

The steady Spring watchdog path allocated zero managed bytes over 600 frames in the .NET 8
test environment in both versions. Its arithmetic was therefore not changed. This result
does not measure allocations in Koikatu's old Mono runtime or rule out other FPS causes.

## Re-run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-FleshRuntimeIntegration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-ThighPhysicsController.ps1 -Force
```

For a source comparison, export the baseline's top-level C# files into an isolated folder,
then run the same test executable with `-p:SomaSourceRoot=<absolute baseline folder>`.
Local evidence is kept in ignored `artifacts/issue-12-13/`.

## Runtime acceptance still required

During 1.0.3.9 preparation, no candidate DLL was installed and Studio was not restarted.
The user subsequently authorized installation; 1.0.3.9 was installed with DLL/configuration
backup on 2026-10-20. The original visual/FPS reports still require runtime acceptance.
No original issue was closed and no GitHub release was published.

After authorized backup/install, compare the same scene with 1.0.3.8 and 1.0.3.9:

1. Keep automatic application and drift correction enabled. Load the same BP bodies,
   leave the state active for at least 60 seconds, then repeat body swaps, clothes previews,
   clear shape, and Spring/Chain mode changes. Verify BP state remains stable and unshared
   body parts retain motion.
2. Include characters with missing or inactive flesh bones. `SOMA_RUNTIME_INIT` now records
   `external_chains` and `discovery_retry`; retries must stop at 3 instead of repeating
   indefinitely. Reloading a character should reset discovery normally.
3. Capture fresh frame-time/FPS and `FPC_PERF` logs with the same character count and motion.
   Solver microseconds alone cannot prove that periodic whole-frame drops are gone.
   Existing frame-rate/metric sandbox gates need fresh 30/60/high-FPS captures before a
   runtime performance claim can be made.

The candidate is ready for runtime verification; the two user-visible reports are not yet
confirmed resolved in the original environments.
