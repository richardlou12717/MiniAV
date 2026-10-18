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
