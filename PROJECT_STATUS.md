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

## M2 in progress (not verified)
- New branch: feature/m2-scan-foundation.
- Added optional MaxFileBytes preflight size check and Core regression tests; local Windows run still pending.
- Scan limit currently checks FileInfo.Length before opening, not bytes actually read; cannot guarantee a hard cap on concurrently growing files.
- WPF interface, progress, logs and UI cancel remain M2 deliverables.

## Next work (M2 planning)
1. Create a new feature branch from current main for M2 work.
2. Design file-byte and scan-time budgets; retain fail-closed incomplete-scan semantics.
3. Consider real Windows ACL-denied and interactive Ctrl+C verification.
4. Continue refining CLI scanning and tests before optional GUI development.

## Collaboration
- ChatGPT acts as architect/developer; user verifies on Windows.
- Prefer feature branches and PRs, maintain stable `main`.
- Never commit personal files, malware samples, secrets or quarantine content.

## Next-session starter
"Continue MiniAV in richardlou12717/MiniAV. Read PROJECT_STATUS.md on feature/m2-scan-foundation, then proceed with M2."
