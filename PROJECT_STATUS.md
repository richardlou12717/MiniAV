# MiniAV — Project Status / Cross-session Handoff

Last updated: 2026-10-17
Status: **M0, M1, M2 merged; M3 adapter, opt-in CLI and WPF integration committed on feature branch (unmerged; latest WPF CI pending).**

## Source of truth and branches
- Private GitHub repository: https://github.com/richardlou12717/MiniAV
- Stable branch: `main`. M3 design branch: `feature/m3-clamav-adapter` (not merged).
- M1: PR #1 merged on 2026-10-17 (`d9efa61bcb9de3d7c7889a9fb53157a5da083990`).
- M2: PR #2 merged on 2026-10-17 (`64e366996d19e4d2e02bd3540326c883b57f35e5`).
- M2 feature branch `feature/m2-scan-foundation` is historical; do not develop M3 on it.
- User Windows working directory: `C:\Projects\MiniAV`.
- GitHub files are authoritative; read them anew in each session.

## Product and safety scope
- Windows x64; C# / .NET 10. Local, on-demand read-only scanning.
- Two front ends: CLI and WPF. Core interfaces and Hash engine are separate projects.
- SHA-256 signatures are harmless *demo-only*; `NO_MATCH` never means a file is safe.
- No real malware signature coverage, no auto deletion, execution, moving, quarantine, Defender replacement, driver, or background telemetry.
- No automatic file/path uploads or disk logs. Manual WPF log export is explicitly user initiated.

## Implemented (M0–M2)
- `src/MiniAV.Core`: `IScanEngine`, `ScanCoordinator`, request/results/outcome, cancellation and fail-closed error reporting.
- Limits: optional `MaxFiles`, actual streaming `MaxFileBytes` (with file-length preflight), optional overall `MaxScanDuration`. Timeout/limit errors are **incomplete scans**, not clean results.
- `src/MiniAV.Engines.Hash`: async SHA-256 with demo JSON signatures.
- `src/MiniAV.Cli`: `--path` and `--db`; exit 0=not matched without errors, 1=demo match, 2=input error, 3=scan incomplete/error, 130=user Ctrl+C.
- `src/MiniAV.Wpf`: browse file/folder/database, optional resource limits, live counts, results, cancel/restart, explicit Save log.
- UI results: batched 100ms / up to 400 results per update; bounded pending result queue (2,000), about 500,000 display characters. Older entries may be omitted; exported display log says `PARTIAL DISPLAY LOG` if truncated. Not a complete forensic/audit log.
- `ScanCloseGate`: defers WPF window close while scan is running and requests cancellation; Core regression deterministically verifies worker-exit ordering. Native WPF Close event is not independently UI-automated.
- No reparse-point traversal; report Error/Skipped distinctly from NoMatch.

## Verified and accepted (2026-10-17)
- User Windows: Release Core/CLI builds, full `scripts/regression-test.ps1` including cancel, simulated read/access errors, MaxFiles, size/time, close lifecycle.
- User Windows: Release WPF build; UI fixture scan, browse, counters, manual log export, all three limit fields and blank/unlimited behavior.
- User Windows: large directory (more than 18,000 results), mid-scan cancellation, restart after cancel, bounded UI log/export behaviors.
- GitHub Actions: two passing checks for final M2 PR head `976b96617c018152d2a26974a0e2f725f0e7c04d`; Windows pipeline includes CLI & WPF Release builds and Core/CLI regression.
- M2 merged with explicit user authorization. No need to re-merge PR #2.

## Known limitations / deferred work
- No actual antivirus detection coverage; no ClamAV implementation yet.
- WPF displayed and saved logs are bounded, possibly partial. A full streaming report requires a separate design and explicit opt-in.
- Native UI automation, real ACL-denied integration, and manual CLI Ctrl+C checks may be added later; not represented as completed.
- File system can change while hashing; scan results are point-in-time. No quarantine or remediation.
- No claim of production-grade security certification or full testing.

## Next milestone: M3 (design in progress)
1. Read this status, `docs/02_ARCHITECTURE.md`, `docs/03_SECURITY_MODEL.md`, `docs/05_DECISIONS.md`, `docs/06_ENGINEERING.md` and latest related code.
2. M3 branch created from `main`; leave `main` stable.
3. M3 design is in `docs/M3_CLAMAV_ADAPTER_DESIGN.md`; initial `src/MiniAV.Engines.ClamAV` process runner, parser, and `tests/MiniAV.ClamAV.Regression` fake-runner suite now exist on feature branch; implementation remains unverified until Windows CI/local tests. executable discovery/installation assumptions, process isolation, safe ArgumentList, scan inputs, structured result/error mapping, exit codes, timeouts/cancellation, resource caps.
4. Review ClamAV licensing/distribution, signature source/authenticity, update/rollback and offline behavior before implementing updater.
5. Initial fake-runner suite and adapter committed; `scripts/regression-test.ps1` includes new tests. Draft PR #3 is open. Run Windows CI/local checks; extend process cancellation/output-limit integration tests before accepting.
6. Adapter is opt-in via CLI and WPF (engine selector, ClamAV executable and optional database folder); latest WPF implementation not yet CI/local UI verified. Keep feature optional/read-only. Validate ClamAV flags and resource policy with real executable before exposing UI; no silent Defender changes, download, quarantine or execution of scanned files.

## Collaboration
- ChatGPT is project architect/developer; user runs Windows verification.
- Use feature branches and PRs, document code changes and real test outcomes.
- Never commit personal files, malware samples, secrets, private scan logs, or quarantine content.

## Next-session starter
"继续 MiniAV 项目。GitHub 仓库 richardlou12717/MiniAV；请先读取 main 的 PROJECT_STATUS.md，再读取必要架构文档与代码，从 M3 的 ClamAV 独立进程适配器方案开始。"

## M3 implementation notes (2026-10-17)
- New `src/MiniAV.Engines.ClamAV` implements `IScanEngine` with separate `clamscan.exe`, bounded process output, per-file timeout and process-tree termination on cancellation.
- `tests/MiniAV.ClamAV.Regression` covers fake exit codes, spoofed output, errors, and Windows cmd.exe subprocess lifecycle (timeout/cancel/output-cap). Integrated into `scripts/regression-test.ps1` alongside a CLI missing-executable fail-closed test.
- CLI opt-in via `--engine clamav --clamav-exe <absolute-path> [--clamav-db <absolute-directory>]`; previous `--path ... --db ...` still uses demo hash engine.
- CI failure on `22d880a` was a test assertion string mismatch (`unverified` vs `not verified`), corrected in `03261d4`; one corrected-head Windows CI run succeeded. Later commits require new checks.
- Known gaps: WPF opt-in not implemented; memory Job Object cap, actual ClamAV interoperability/scan-completeness, genuine coverage, real installation, database authenticity/freshness, and release-grade validation are NOT complete. Do not merge yet.

## M3 WPF wiring (2026-10-17)
- WPF now has a Hash default / optional ClamAV selector, executable picker and optional database folder picker. Both front ends use the existing ScanCoordinator. WPF scan cancellation/close lifecycle still uses prior gate.
- All code is feature-branch only, Draft PR #3. Windows CI already passed earlier adapter/CLI and child-process lifecycle tests; **the WPF integration commit has not yet been verified**, nor has actual installed ClamAV compatibility.
- Next: validate latest Windows CI, test actual WPF UI interactions locally (select/reset, missing engine, cancel/close/restart, report labels), then assess ClamAV internal scan limits and release-grade memory/CPU caps. Keep `main` stable.

## M3 safety hardening follow-up
- Latest WPF implementation head `cab21b56` passed two Windows CI runs (2026-10-17 local / 2026-10-18 UTC). No real ClamAV execution yet.
- ClamAV command now supplies `--alert-exceeds-max=yes` and maps `Heuristics.Limits.Exceeded` detections to `Error` (incomplete), not malware or NoMatch. Source: official clamscan man page. Regression added; latest CI pending.
- Limit alerts cover certain internal scan limits only; not proof that every archive member was inspected. Real-engine interoperability, further scan-limit behavior, resources and Windows UI manual checks remain release gates.

## M3 follow-up — acceptance planning
- The latest passing Windows CI checks cover earlier WPF/CLI integration and the initial ClamAV scan-limit alert flag. Further follow-up test head may still be running.
- Added specific limit-alert regression and `docs/M3_WINDOWS_ACCEPTANCE.md` with local CLI, WPF, and real ClamAV verification steps.
- **Not yet implemented**: hard memory/CPU process cap, complete scan coverage attestation, verified real ClamAV installation/database, native WPF automation. PR #3 remains Draft.
