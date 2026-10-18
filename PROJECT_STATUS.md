# MiniAV — Project Status / Cross-session Handoff

Last updated: 2026-10-17
Status: **M0, M1, M2 complete and merged; M3 not started.**

## Source of truth and branches
- Private GitHub repository: https://github.com/richardlou12717/MiniAV
- Stable branch: `main`. Current development milestone: M3 planning only.
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

## Next milestone: M3 (not started)
1. Read this status, `docs/02_ARCHITECTURE.md`, `docs/03_SECURITY_MODEL.md`, `docs/05_DECISIONS.md`, `docs/06_ENGINEERING.md` and latest related code.
2. Create a new feature branch from latest `main`; leave `main` stable.
3. Design optional independent-process ClamAV adapter: executable discovery/installation assumptions, process isolation, safe ArgumentList, scan inputs, structured result/error mapping, exit codes, timeouts/cancellation, resource caps.
4. Review ClamAV licensing/distribution, signature source/authenticity, update/rollback and offline behavior before implementing updater.
5. First develop deterministic fake-process tests with harmless fixtures, then implement adapter and run Windows CI/local checks; propose a new Draft PR.
6. Keep feature optional and read-only; no silent Defender changes, download, quarantine or execution of scanned files.

## Collaboration
- ChatGPT is project architect/developer; user runs Windows verification.
- Use feature branches and PRs, document code changes and real test outcomes.
- Never commit personal files, malware samples, secrets, private scan logs, or quarantine content.

## Next-session starter
"继续 MiniAV 项目。GitHub 仓库 richardlou12717/MiniAV；请先读取 main 的 PROJECT_STATUS.md，再读取必要架构文档与代码，从 M3 的 ClamAV 独立进程适配器方案开始。"
