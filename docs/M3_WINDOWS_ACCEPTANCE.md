# M3 Windows Acceptance Checklist (not yet signed off)

This checklist is for **local Windows verification** after GitHub CI passes; it is not evidence of tests having been run. Keep PR #3 Draft until all required items are observed and documented.

## Preconditions
- Checkout `feature/m3-clamav-adapter` into a clean working tree.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1`.
- Run `dotnet build .\src\MiniAV.Wpf\MiniAV.Wpf.csproj -c Release`.
- Use ordinary harmless files. No malware specimen is required.
- ClamAV is **optional** and should only be installed manually from verified official sources. MiniAV does not fetch binaries or signatures.

## CLI
1. Existing demo command `dotnet run --project src/MiniAV.Cli -- --path tests/fixtures --db signatures/demo-hashes.json`: existing demo results and exit codes unchanged.
2. `dotnet run --project src/MiniAV.Cli -- --path tests/fixtures/ordinary-sample.txt --engine clamav --clamav-exe C:\\invalid\\clamscan.exe`: exit 3, `ERROR`, never `NO_MATCH`.
3. Supply a real, verified absolute path to `clamscan.exe`, and optionally a local, verified ClamAV database directory. Test a harmless ordinary file, a path with spaces, and a Unicode path.
4. Invalid or absent database, unsupported flags, and ClamAV execution failure must not yield exit 0.
5. Run a larger ordinary input and cancel with Ctrl+C; expect 130 and child termination, not a success report.
6. Confirm no target is moved/deleted and Defender remains active.

## WPF
1. Launch with default Hash selection. Browse a demo file/database and confirm previous M2 behavior.
2. Select ClamAV. Hash database controls disable; ClamAV executable and optional database-directory controls enable.
3. Select missing ClamAV executable, scan a harmless target and verify **incomplete/error** status, not successful clean scan.
4. Test real ClamAV only if installed. Confirm matched/no-match/error labels, counts and report metadata (version is explicitly unverified).
5. Start a scan; verify controls lock, cancel works, restart works, and close requests wait until subprocess cleanup.
6. Confirm Save Log is manual and bounded; no automatic logs or uploads are created.
7. Switch back to Hash, verify its controls re-enable and ClamAV inputs disable.

## Security gates before merge
- **Real engine compatibility:** verify actual supported ClamAV version and every CLI flag on Windows.
- **Incomplete coverage:** document limits for archives, encrypted containers, oversized files, parser failures, and `Heuristics.Limits.Exceeded` mapping; no safety guarantee from `NO_MATCH`.
- **Resource isolation:** process timeout + output cap exist; a 2 GiB per-process and aggregate Job committed-memory limit via Windows Job Object is newly implemented but CI/local validation pending; hard CPU cap and full sandboxing remain unimplemented.
- **Distribution/licensing:** no ClamAV binary distributed in M3; review before bundling. Signature provenance/freshness/update/rollback are deferred.
- **UI manual tests:** no automated native WPF test exists.
- Document observed command, platform, ClamAV/database version, result, and unresolved limitations in PR #3 before any merge.

## Performance note
The initial coordinator invokes one `clamscan` process per file and reloads the signature database each time. Do **not** present it as practical large-directory real-time protection. A persistent daemon or batch interface requires its own architecture/security review.

## Job Object implementation caveats (requires testing)
- Windows Job Object now applies a **2 GiB per-process committed-memory ceiling** and kill-on-job-close; failure to attach is fail-closed. This bounds committed memory for processes that remain in the Job, but is not a CPU limit or sandbox; verify descendant membership and startup race behavior.
- Process.Start occurs before Job Object assignment; the small pre-assignment execution window remains. Release-hardening should consider starting suspended via native Windows process creation, assigning the Job before resuming, and testing under nested Jobs/enterprise policy.
- Verify on Windows 11 x64 that child processes run under a Job Object, memory exhaustion returns an incomplete/error result, cancel/timeout reaps descendants, and Job assignment failures do not produce NoMatch.

- Job Object flags now include `JOB_OBJECT_LIMIT_JOB_MEMORY` in addition to `JOB_OBJECT_LIMIT_PROCESS_MEMORY` and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. Check aggregate committed memory for child descendants in a controlled local stress test; CI's harmless subprocess tests do not prove this cap is enforced under memory pressure.

## Optional real ClamAV smoke (manual only)
Once ClamAV and its signatures have been installed and verified by the user, run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\clamav-local-smoke.ps1 -ClamScanExe "C:\Path\To\clamscan.exe" -DatabaseDirectory "C:\Path\To\database"
```
Omit `-DatabaseDirectory` to use ClamAV's default configuration. This script scans only an automatically created harmless text file, reports errors honestly and cleans up temporary fixtures. It never installs/updates ClamAV, changes Defender, or scans arbitrary personal files. A single clean smoke does not establish scanning completeness or malware coverage.

## User-reported WPF acceptance — 2026-10-18
- Environment: Windows, separately installed ClamAV 1.5.4 and database version 28157 (version reported by user; not attested by MiniAV).
- Missing executable (manually entered): scanned the two harmless `tests/fixtures` files; WPF displayed `Finished with errors — scan incomplete (exit 3)`, `Processed: 2`, `Errors: 2`, `No match: 0`, with `ClamAV executable not found.` on each line. **PASS**, screenshot observed.
- Real ClamAV WPF path `C:\\Program Files\\ClamAV\\clamscan.exe`, optional database directory blank: `Finished (exit 0)`; `Matched=0 NoMatch=2 Skipped=0 Errors=0` on two harmless fixtures. **PASS**, screenshot observed.
- User subsequently reports **PASS** for Hash/ClamAV control switching, scan cancellation, restart after cancellation, close-during-scan behavior/process cleanup, and manual Save Log. These items are user-reported manual acceptance, not independent process traces or native UI automation.
- The selected executable picker only chooses existing `clamscan.exe` entries; invalid-executable test used the editable textbox instead. This is expected.
- Do **not** infer verified detection effectiveness, archive completeness, precise signature freshness, the 2 GiB memory ceiling under actual pressure, or absence of all descendant processes from these WPF results.

## Next controlled resource-verification gate
- First inspect current Job limits with a dedicated **harmless test worker** and a deliberately low **test-only** memory cap; do not exhaust the machine's memory or force real ClamAV to allocate 2 GiB.
- Verify both configured per-process and aggregate Job committed-memory limits, a failure/non-clean result on exceeded budget, cleanup on cancellation/timeout, and a descendant process if applicable.
- Guard against flaky CI, test nested Job policy, and clearly separate *configured limits*, *observed enforcement*, and *unsupported guarantees*. No hard CPU cap or launch-suspended guarantee exists.
- Maintain PR #3 as Draft until resource testing and remaining security gates are reviewed.

### Opt-in bounded Job memory pressure test (not yet executed)
After Windows CI validates compilation and the standard regression suite, run on Windows:

```powershell
dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj --configuration Release -- --job-memory-stress
```

The test launches a harmless .NET child, waits for a READY handshake, attaches a Windows Job Object with a **192 MiB test-only committed-memory limit**, then asks the child to touch up to **256 MiB** of pinned memory. A nonzero exit without `TARGET_REACHED` is reported as a limit-rejection observation. The fixture has a 30-second timeout and kills its child in a finally block. It does not alter the adapter's production **2 GiB** setting, run ClamAV, download signatures, or scan personal files. Do not interpret this test as proof of exact memory accounting, aggregate descendant limits, or closure of the production startup-before-Job-attachment race. Failures may reflect environment constraints and should be diagnosed rather than bypassed.

### User Windows Job memory pressure result — 2026-10-18
- On the M3 feature branch at `2a265af`, user executed `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj --configuration Release -- --job-memory-stress`.
- **PASS (user-observed):** `ClamAV Job Object low-budget memory stress passed (192 MiB cap / 256 MiB target)`; worker exit `42`, output `MEMORY_LIMIT_REACHED`.
- This is evidence of observed low-budget memory-allocation rejection while a Job with both per-process and aggregate committed-memory limits was applied. Since both thresholds were equal, the test cannot isolate aggregate limit enforcement. It does not establish the production 2 GiB behavior or descendant aggregate limits.
- Next: distinct per-process vs aggregate memory-limit regression using controlled child workers; preserve safety/cleanup/timeout, and do not use real ClamAV or unbounded allocations.

### Opt-in aggregate Job memory test (pending local Windows verification)
Run after checking CI:
```powershell
dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj --configuration Release -- --job-aggregate-stress
```
The fixture attaches a parent to a **256 MiB Job limit** (both configured per-process and aggregate). The parent holds **112 MiB** in touched allocations and spawns a descendant, which attempts **160 MiB**. Each worker requests less than the per-process budget, while their combined target exceeds the Job budget. The test is opt-in and includes a 40-second parent timeout and cleanup. Treat PASS as an observed aggregate-limit rejection in this setup, not proof against all Windows policy/launch races or of exact committed-memory accounting. Does not run ClamAV or change the production 2 GiB cap.

### User Windows aggregate memory test result — 2026-10-18
- User executed `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj --configuration Release -- --job-aggregate-stress`.
- **PASS (user-reported output):** `ClamAV Job Object aggregate memory stress passed (256 MiB Job limit; parent 112 MiB + descendant 160 MiB)`.
- Evidence covers a controlled parent/descendant Job test with a detected allocation rejection. It does not independently measure actual committed memory or prove the real ClamAV 2 GiB limit under load. Test fixture synchronization and independent worker baselines can be strengthened further.
- Next security gate: review launch-before-Job-assignment race and plan suspend-before-assignment implementation; no merge authorization.

## Suspended process launch prototype (not production; pending Windows local test)
- Opt-in test: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --suspended-launch-test`.
- A harmless cmd.exe child is created with Win32 `CREATE_SUSPENDED`, assigned to `ClamJob`, confirmed in a Job, and only then resumed. Failure cleanup terminates and closes native handles. This is **only** a regression prototype; production `ClamProcessRunner` still uses `Process.Start` followed by `ClamJob.Attach` and thus retains the startup window.
- Before replacing production launcher, design safe inherited anonymous pipes with explicit inherited-handle allowlist (`STARTUPINFOEX` preferred), argument quoting, lifetime management, cancellation, bounded stdout/stderr, timeout/kill, nested Jobs, and independently verify real ClamAV compatibility. Keep PR Draft.

### User Windows suspended-launch prototype result — 2026-10-18
- User ran `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --suspended-launch-test`.
- **PASS (user-observed):** `Suspended-launch Job-before-resume regression passed`.
- This verifies only a harmless `cmd.exe` prototype through `CREATE_SUSPENDED`, Job attachment, and `ResumeThread`. **Production ClamProcessRunner remains unchanged and continues to have a short process-start-before-Job-assignment window.** No real ClamAV native-launch interoperability, redirected-pipe or cancellation validation was performed by this prototype.

### Suspended-launch failure cleanup (new opt-in regression; unverified locally)
Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --suspended-failure-test`. This creates a harmless `cmd.exe` in suspended state, deliberately does not attach or resume it, then calls `TerminateProcess` and waits up to five seconds for exit. PASS proves the test-only failure cleanup path reaped this child. It does not prove production launcher correctness. This remains a preparatory test; production still uses `Process.Start()` and attaches the Job afterward.

### Production native-launch integration design gate
Do not switch the production path until the native launcher handles `STARTUPINFOEX` with `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` (only the intended stdin/stdout/stderr pipe endpoints inherited), Windows command-line escaping, reliable output draining and 32,768-character caps, cancellation/timeout before and after ResumeThread, Job assignment before ResumeThread, parent/child handle ownership, and `CREATE_SUSPENDED` cleanup on all failures. Verify harmless subprocess regressions, real ClamAV 1.5.4, WPF cancel/close, Windows CI, and no changes to Defender or user files. Treat nested Job incompatibility as an incomplete/error outcome rather than weakening resource bounds.

### User Windows suspended failure-path test — 2026-10-18
- User ran `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --suspended-failure-test` after commit `3b3d45a`.
- **PASS (user-observed):** `Suspended-launch failure-cleanup regression passed`.
- Scope: harmless suspended cmd.exe terminated and reaped on test failure path. Does not validate production runner, redirected pipes, or cancellation integration. Full regression and production integration remain pending.

### User Windows full regression — 2026-10-18
- User executed `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1` after the suspended-launch failure regression repair.
- **PASS (user-observed):** Release Core/Hash/ClamAV/CLI build; Core cancellation/read-error/file-count/file-size/scan-time/window-close tests; ClamAV Job membership and kill-on-close, real subprocess lifecycle, fake-runner regression, overall regression script.
- The two suspended-launch tests are separately opt-in and were previously user-verified; they are not run by this default script. Production still uses `Process.Start()` then `ClamJob.Attach()`; no claim of race closure.

### User Windows regression including suspended launch — 2026-10-18
- User reran `scripts/regression-test.ps1` after suspended-launch regressions were included in the normal suite.
- **PASS (user-observed):** Release build; six Core regressions; `Suspended-launch Job-before-resume regression passed`; `Suspended-launch failure-cleanup regression passed`; ClamAV Job membership/close, subprocess lifecycle, fake runner, and overall regression tests all passed.
- This verifies only existing production behavior and isolated launch tests. Native suspended launch with redirected pipes is still unimplemented; no production race fix is claimed.

### User Windows native pipe and argument regression — 2026-10-18
- User reported successful `scripts/regression-test.ps1` Release build and all regressions after standalone `WindowsChildPipe` and `WindowsArgumentEncoding` tests were integrated.
- **PASS (user-observed):** `Windows CreateProcess argument quoting regressions passed`, `Windows child pipe inheritance regressions passed`, both suspended launch tests, ClamAV Job membership/close, real subprocess lifecycle, fake runner, all Core regressions, final `Regression tests passed`.
- Scope is standalone quoting/handle-inheritability verification; redirected process I/O and explicit `STARTUPINFOEX` handle list are not yet tested. Production `ClamProcessRunner` unchanged.

### User Windows pipe transfer regression — 2026-10-18
- User ran full `scripts/regression-test.ps1` after the bidirectional byte-transfer regression was introduced.
- **PASS (user-observed):** Release build; Core tests; `Windows CreateProcess argument quoting regressions passed`; `Windows child pipe inheritance and byte-transfer regressions passed`; both suspended launch regressions; ClamAV Job membership/close, subprocess lifecycle, fake runner; overall regression.
- This validates in-process pipe endpoint transfer, not yet `STARTUPINFOEX` process inheritance or real child I/O.

### Native STARTUPINFOEX redirected I/O local test — 2026-10-18
- **PASS (user-observed)**: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-io-test` printed `Windows STARTUPINFOEX redirected child IO regression passed` after commit `4f9f9e3` corrected cmd.exe `/c` syntax.
- Standalone harmless cmd.exe fixture verifies three-endpoint explicit inheritance list, suspended creation, Job assignment before resume, stdout/stderr and completion. Not production scanner; cancellation, timeout, high-volume streams, orphan/handle-leak behavior and real ClamAV remain unverified for new launcher.

### M3.1 acceptance and remaining caveats — 2026-10-19

User-observed Windows results (isolated native launcher, NOT production ClamAV):
- Basic native launcher after corrected Job-close/wait-before-fallback-terminate order — PASS. Earlier immediate fallback failed with Win32 Access Denied (5).
- Pre-resume injected fault — PASS (earlier run); no new re-run supplied in the latest batch.
- Active stdout/stderr cancellation — PASS (five runs).
- Descendant removal after cancellation — PASS; after timeout and concurrent stdout/stderr overflow — PASS.
- 30 regular starts after warm-up — PASS, handle delta +4. First un-warmed experiment had failed at +50; this is retained for historical accuracy.
- 30 alternating cancellation/timeout/overflow runs **after latest cleanup fix** — PASS; handles baseline 267, run 10 272 (+5), run 20 273 (+6), run 30 275 (+8). Positive delta does not establish absence of a slow leak.
- Full `scripts/regression-test.ps1` after latest cleanup fix — PASS; Release build in 1.9s, all normal Core/Windows suspended/Job/ClamAV regression categories passed.
- GitHub Windows CI not established green for latest head at handoff (earlier run seen in_progress).

Open safety blocker: current `CreatePipe` outputs are synchronous. `isAsync:true` failed in Windows. Reader task joining currently has a five-second bound, but on failure can still leave an in-flight read while stream ownership unwinds. Do not represent this as a complete deterministic cleanup guarantee. Leave PR #3 Draft and do not switch production runner.
