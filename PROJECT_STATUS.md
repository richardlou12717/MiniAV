# MiniAV — Project Status / Cross-session Handoff

Last updated: 2026-10-17
Status: M0 and M1 complete; M2 foundation started on feature/m2-scan-foundation. PR #1 merged into main (merge commit d9efa61bcb9de3d7c7889a9fb53157a5da083990).

## Source of truth
- Repository: https://github.com/richardlou12717/MiniAV (private)
- Stable branch: `main`
- M1 feature branch: `feature/m1-modular-scanner` (merged; do not use for new development)
- PR: https://github.com/richardlou12717/MiniAV/pull/1
- User's Windows working directory: `C:\Projects\MiniAV`
- Always consult the latest repository contents before changing code.

## Product scope
- Windows x64, C# / .NET 10, local, on-demand read-only scanning.
- CLI first, WPF later; optional independent ClamAV adapter later.
- No file deletion, execution, quarantine or Defender replacement in this milestone.
- Demo signatures only: `NO_MATCH` does not mean safe.

## Completed on M1 feature branch
- `src/MiniAV.Core/`: `IScanEngine`, `ScanRequest`, `ScanResult`, `ScanOutcome`, `ScanCoordinator`.
- `src/MiniAV.Engines.Hash/`: asynchronous SHA-256 streaming and demo database loader.
- CLI preserves existing `--path`, `--db`, output and exit code semantics.
- `scripts/regression-test.ps1`: CLI positive/negative/missing path/invalid signature DB checks.
- `.github/workflows/windows-ci.yml`: Windows build and regression runner.
- Invalid SHA-256 database exception is now converted to CLI exit code 2 instead of a crash.
- M1 merged to main on 2026-10-17 via PR #1 after local regression tests and two passing Windows CI checks.

## User-verified results (2026-10-17)
- Debug build of all three projects successful.
- Release build of all three projects successful.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1` reported `Regression tests passed`.
- Previous intermediate test failures uncovered and fixed stderr handling in PowerShell and uncaught `InvalidDataException`.
- Windows Release build and extended regression tests (LF/CRLF signatures, junction skipping, invalid DB, missing target) passed locally after commit 2949d24.
- User screenshot verified two GitHub Actions checks passed for M1 PR #1 prior to merge.

## Latest Windows verification
- User confirmed Release build, Core cancellation regression tests and full regression tests passed (2026-10-17). CLI Ctrl+C interaction itself remains untested manually.

## Latest verification of scan limits and errors
- User confirmed Release build (1.9 seconds), Core cancellation regression tests, simulated I/O and access-denied tests, MaxFiles tests and full CLI regression tests all passed on Windows.
- Real ACL-denied tests, interactive Ctrl+C and time/byte limits remain future validation and design work.

## M2 in progress (first increment Windows verified)
- New branch: feature/m2-scan-foundation.
- Added optional MaxFileBytes preflight size check and Core regression tests; Windows Release build and full regression confirmed by user (3.3 seconds).
- Scan limit currently checks FileInfo.Length before opening, not bytes actually read; cannot guarantee a hard cap on concurrently growing files.
- WPF interface, progress, logs and UI cancel remain M2 deliverables.

## M2 WPF scaffold (pending Windows verification)
- Added src/MiniAV.Wpf with a read-only scan window, Start/Cancel, result log, indeterminate progress and processed-item count.
- WPF project is not included in the CLI regression script; build and launch separately on Windows.
- User verified Windows WPF build, launch and fixture scan: 1 MATCHED, 1 NO_MATCH, 0 errors, 2 processed, exit 1. Cancellation and longer-running scans remain unverified.

## M2 WPF interaction increment (pending Windows verification)
- Added file/folder/database browse dialogs, live per-status counts and Save log (manual export only).
- No automatic log collection; report saving requires an explicit user action.
- WPF compile and UI behaviors for this increment remain unverified; retain Draft PR #2.

## M2 WPF interaction verification (2026-10-17)
- User confirmed Browse controls, result counters, Save log, and 2-file fixture scan work on Windows.
- Screenshot: Finished exit 1, processed 2, matched 1, no-match 1, skipped 0, errors 0.
- Cancellation during an ongoing scan and restart behavior remain unverified.

## M2 WPF cancellation/close lifecycle (pending Windows verification)
- Commit 1c6ec30: cancelling a running scan now disables repeat Cancel requests and labels the scan incomplete.
- Closing the WPF window during a scan requests cancellation; after the scan finishes or cancels, the pending window close automatically completes.
- Cancelled scans append an explicit `CANCELLED — scan incomplete` report line; existing Start/Cancel and count behavior is preserved.
- Windows WPF build, interactive cancellation and window-close tests are NOT yet verified for this increment.
- Next: test cancellation during a long scan, close-while-scanning, and restart after cancellation; then address per-file byte/time budgets and scale/performance.

## M2 interactive cancellation verification (2026-10-17)
- User confirmed manual WPF cancellation during an ongoing scan succeeded on Windows.
- Close-while-scanning and restart-after-cancellation remain separately unverified.

## M2 streaming byte budget (pending Windows verification)
- Core now passes `MaxFileBytes` to `IScanEngine.ScanFileAsync` instead of relying only on `FileInfo.Length` preflight.
- SHA-256 engine hashes asynchronously in bounded chunks and reads at most one byte beyond the limit to identify files that have grown; over-budget files report `Error` instead of `NoMatch`.
- Regression executable includes Hash engine boundary cases (exact size, size minus one, unbounded).
- Changes are committed on feature/m2-scan-foundation; Windows compile and regression are NOT yet verified. No .NET SDK is available in the development container.
- The WPF UI currently does not expose MaxFileBytes; next step includes considering user-facing limits and scan-time budgets.

## Next work (M2)
1. Verify WPF cancellation / closing behavior on Windows; leave PR #2 in Draft until verified.
2. Design actual bytes-read and scan-time budgets; retain fail-closed incomplete-scan semantics.
3. Consider real Windows ACL-denied and interactive CLI Ctrl+C tests.
4. Review WPF per-file Dispatcher overhead before scaling to large directory trees.

## Collaboration
- ChatGPT acts as architect/developer; user verifies on Windows.
- Prefer feature branches and PRs, maintain stable `main`.
- Never commit personal files, malware samples, secrets or quarantine content.

## Next-session starter
"Continue MiniAV in richardlou12717/MiniAV. Read PROJECT_STATUS.md on feature/m2-scan-foundation, then proceed with M2."

## M2 validation and time budget (2026-10-17)
- User confirmed Windows regression script and WPF Release build passed after streaming byte-budget changes.
- Added optional MaxScanDuration to ScanRequest. It uses linked cancellation; exceeding the deadline yields an incomplete-scan Error and exit 3. Explicit user cancellation remains distinct.
- Added Core regression tests for deadline expiry, user cancellation, and invalid duration.
- New time-budget changes have NOT yet been Windows-verified. No UI or CLI duration argument yet.

## M2 Windows verification and WPF limits UI (2026-10-17)
- User confirmed Release CLI build, entire regression suite (including Core scan-time regression), and Release WPF build all passed.
- Added optional WPF fields for Max files, Max file size (MiB), Max scan time (seconds), blank = unlimited.
- WPF validates positive integer limits before scan, passes values to ScanRequest, disables input fields while scanning, and labels any scan with errors as incomplete.
- WPF limits UI increment is pending Windows build and interactive verification; do not merge PR #2 yet.
- Suggested test: Max files=1 on tests/fixtures -> exit 3 and limit error; Max file size=1 MiB on a file larger than 1 MiB -> exit 3; Max scan time=1 seconds on a large directory -> incomplete timeout; blank fields -> existing successful scan.
