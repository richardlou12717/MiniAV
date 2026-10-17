# MiniAV — Project Status / Cross-session Handoff

Last updated: 2026-10-17
Status: M0 complete. M1 modular scanner compiled and extended regression tests passed on the user's Windows machine. PR #1 is draft and not merged.

## Source of truth
- Repository: https://github.com/richardlou12717/MiniAV (private)
- Stable branch: `main`
- Working branch: `feature/m1-modular-scanner`
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
- Work is on draft PR #1; main is untouched.

## User-verified results (2026-10-17)
- Debug build of all three projects successful.
- Release build of all three projects successful.
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1` reported `Regression tests passed`.
- Previous intermediate test failures uncovered and fixed stderr handling in PowerShell and uncaught `InvalidDataException`.
- Windows Release build and extended regression tests (LF/CRLF signatures, junction skipping, invalid DB, missing target) passed locally after commit 2949d24.\n- No independent GitHub Actions success result confirmed here; check PR before merging.

## Next work
1. Add explicit reparse-point/junction test cases on Windows and verify skipping.
2. Test cancellation and error semantics; consider scan limits before WPF.
3. Strengthen positive LF/CRLF and exit-code/output assertions.
4. Inspect GitHub Actions checks and local results; merge PR only when ready.
5. Update this file and changelog after verified milestones.

## Collaboration
- ChatGPT acts as architect/developer; user verifies on Windows.
- Prefer feature branches and PRs, maintain stable `main`.
- Never commit personal files, malware samples, secrets or quarantine content.

## Next-session starter
"Continue MiniAV in richardlou12717/MiniAV. Read PROJECT_STATUS.md and current PR #1 before proceeding."
