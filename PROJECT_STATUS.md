# MiniAV — Project Status / Cross-session Handoff

**Updated:** 2026-10-18  
**Active milestone:** M3 — optional ClamAV out-of-process adapter; production native-launch hardening in progress.  
**State:** M0/M1/M2 merged into stable `main`. M3 remains **unmerged** on `feature/m3-clamav-adapter`, [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) (confirmed open, draft, unmerged on 2026-10-18). **Do not merge without explicit user authorization.**

## Repository and working agreement
- Private repo: https://github.com/richardlou12717/MiniAV
- Feature branch: `feature/m3-clamav-adapter`; stable `main` has not received M3 work.
- User's local Windows directory: `C:\Projects\MiniAV`; user runs local Windows/real-ClamAV/WPF checks; assistant develops and documents on GitHub feature branch.
- The files in the repo are source of truth. In a new chat, read **this feature branch version** of `PROJECT_STATUS.md` first, then `docs/M3_NATIVE_LAUNCHER_PLAN.md`, `docs/M3_WINDOWS_ACCEPTANCE.md`, current ClamAV runner and regression fixtures. Do not rely on stale `main` status for M3 progress.
- Never commit private scan outputs, personal files, malware samples, credentials, or automatic quarantine. Do not silently alter Microsoft Defender.

## Product scope and stable M0–M2
- Windows x64, C#/.NET 10. Local read-only on-demand file/folder scanning. CLI and WPF; Core ScanCoordinator and demo SHA-256 Hash engine are separate.
- `main` contains accepted M0/M1/M2. WPF supports browse, live results, cancellation and guarded close, explicit Save Log; bounded display/logs can be partial, not forensic audit logs.
- Scan size/count/time limits; cancellation, access/read failures and partial scans fail closed. No auto-delete, quarantine, driver, background execution, file upload, Defender replacement or promise that `NoMatch` means safe.
- M1 PR #1 and M2 PR #2 were merged with authorization in 2026-10-17.

## M3 implemented on feature branch
- `src/MiniAV.Engines.ClamAV`: opt-in external `clamscan.exe` adapter and parser. CLI `--engine clamav --clamav-exe <absolute path> [--clamav-db <folder>]`; WPF Hash/ClamAV selector, executable and optional DB folder controls.
- Separate clamscan process per file with fixed CLI switches, bounded stdout/stderr (~32,768 chars each), ~3-minute default per-file timeout, cancellation, process-tree termination, fail-closed errors; certain `Heuristics.Limits.Exceeded` paths treated as incomplete, not clean. Best-effort file size/modified-time comparison detects *some* concurrent mutation but does not prove stable content.
- Windows `ClamJob` Job Object with kill-on-close and 2 GiB per-process and aggregate committed-memory ceilings. Local low-memory (192 MiB vs 256 MiB target) and aggregate (parent/descendant under 256 MiB) *opt-in stress fixtures* were reported PASS earlier; these are bounded demonstrations, not proof of all production memory behavior or CPU limits.
- **Important remaining security bug:** production `ClamProcessRunner` uses `Process.Start()` **before** `ClamJob.Attach()`. There is a short execution-before-Job race. New native suspended launcher is not connected to production yet. Do not claim race is fixed.
- Real user-installed ClamAV 1.5.4 (reported database version 28157) passed harmless clean-file smoke test; user also passed real WPF ClamAV NoMatch=2/Errors=0 and invalid-executable Errors=2/NoMatch=0, plus selector/cancel/restart/close/export checks. Signature freshness/authenticity and complete scan coverage remain unverified.

## Native suspended-launch groundwork — verified on Windows (2026-10-18)
- `src/MiniAV.Engines.ClamAV/WindowsArgumentEncoding.cs`: Windows CreateProcess argv quoting; `NativeCommandLineTest` regression checks spaces/quotes/backslashes/Unicode and invalid NUL.
- `src/MiniAV.Engines.ClamAV/WindowsChildPipe.cs`: owned anonymous pipe endpoints; child-only inheritance. `NativePipeRegression` checks both directions' handle flags and binary byte transfer.
- `SuspendedLaunchRegression`: both Job-before-`ResumeThread` and failure cleanup tests integrated into `scripts/regression-test.ps1`.
- `tests/MiniAV.ClamAV.Regression/NativeInheritedIoRegression.cs`: **opt-in only** `--native-io-test` fixture. Uses `STARTUPINFOEX`, `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` for only the three stdio handles, `CreateProcessW(CREATE_SUSPENDED)`, Job assignment before `ResumeThread`, and redirected stdout/stderr via harmless `cmd.exe`.
- **User-observed PASS:** `dotnet run --project .\tests\MiniAV.ClamAV.Regression\MiniAV.ClamAV.Regression.csproj -c Release -- --native-io-test` printed `Windows STARTUPINFOEX redirected child IO regression passed` after commit `4f9f9e3`. Earlier failure was **cmd.exe /c command-language quoting**, corrected in test-only command assembly; general argv encoder remains unchanged.
- **User-observed PASS:** full `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1` after argument, pipe and byte-transfer checks: Core, ClamAV Job/process/fake-runner, Windows argument + pipe + suspended-launch tests, overall regression. **Note:** `--native-io-test` is separately opt-in and was passed afterward; it is not yet in the default regression suite.
- CI last checked: commit `4f9f9e3` had one successful Windows run and another still in progress; latest documentation commit `e16691e` had two in progress. Recheck latest CI; do not infer completion from compilation-only CI (opt-in native I/O test is not default CI execution).


## M3.1 isolated launcher checkpoint (2026-10-18; pending Windows validation)
- Introduced `WindowsSuspendedLauncher.RunAsync` (not wired into `ClamProcessRunner`). Builds an argv-safe command line, creates a child suspended with `STARTUPINFOEX` three-stdio-handle allowlist, attaches `ClamJob` before resume, and drains stdout/stderr concurrently with per-stream character limits.
- Checks cancellation before launch and resume; a linked deadline drives timeout. A fail-fast completion loop reacts to overflow, cancellation, and process/read failures without awaiting unrelated blocked pipe readers before entering cleanup.
- Cleanup closes Job first (kill-on-close), then terminates and waits briefly for a still-active main child, disposes owned pipe/attribute/process/thread resources. A 5-second failed wait is currently only logged with `Debug.WriteLine`; **strong cleanup confirmation and explicit failure reporting remain incomplete**. Pipe readers may still be unwinding after cancellation. Not production-ready.
- Added separately opt-in `--native-launcher-test` for stdout/stderr/exit, pre-launch cancel, running cancel, timeout, and simultaneous output flood. **Not yet run on user Windows or confirmed by CI**. Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-launcher-test`.
- Still outstanding: post-create/pre-resume cancellation injection; orphan/descendant tests; repeated handle-leak checks; robust termination/pipe reader quiescence; Windows acceptance; real ClamAV compatibility and production integration. Prior Process.Start-based runner remains active.

## M3.1 Windows acceptance update (2026-10-18)
- User reported PASS on Windows for `--native-launcher-test`: `M3.1 isolated native launcher smoke, cancellation, timeout and output cap passed`. This is an isolated fixture, not production ClamAV integration or descendant cleanup proof.
- Added `--native-launcher-repeat-test`: 30 harmless PowerShell launches, output assertions and parent handle-count delta diagnostic. Pending local validation; handle count is heuristic.
- Next: repeat result, explicit cleanup failure reporting, reader task shutdown, descendant/orphan and pre-resume fault injection. Production `ClamProcessRunner` unchanged; PR #3 Draft.

## Current milestone boundary
**Completed:** design; ClamAV feature adapter; opt-in CLI/WPF and earlier local smoke; Job memory cap and stress tests; native Windows argv/pipes standalone tests; suspended Job-before-resume and cleanup; working standalone `STARTUPINFOEX` redirected stdout/stderr prototype.

**Not completed:** production-grade reusable native launcher, all failure/timeout/cancellation paths, high-volume output caps and deadlock checks, descendant cleanup/orphan checks under native launch, repeated handle leak runs, real ClamAV compatibility with native launch, production `ClamProcessRunner` integration, post-integration full CLI/WPF and real-engine reacceptance. No proof of exhaustive malware coverage, DB provenance, hard CPU limit, filesystem snapshot, or OS sandbox.

## Next work (start here in new chat)
1. Confirm branch head, Draft PR #3 state and GitHub CI. Review `NativeInheritedIoRegression.cs`, `WindowsChildPipe.cs`, `WindowsArgumentEncoding.cs`, `ClamProcessRunner.cs`, `ClamJob.cs` and `docs/M3_NATIVE_LAUNCHER_PLAN.md`.
2. **M3.1:** extract/test reusable native launcher while preserving explicit stdio handle allowlist and the order `CreateProcessW(CREATE_SUSPENDED)` → Job assignment → `ResumeThread`. Add cancellation before/after resume, timeout, bounded pipe drain, deterministic fail-closed cleanup.
3. **M3.2–3.3:** output flooding/limit, child and descendant killing, fail injection, handle-leak and repeated iterations; Windows CI + local tests. Respect nested Job errors; no unbounded fallback.
4. **M3.4:** only after verified, replace production Process.Start runner; leave parsing, CLI/WPF behavior stable.
5. **M3.5:** rerun full regression, real ClamAV harmless smoke, WPF engine selection, cancel/restart/close and manual export. Keep PR Draft until user authorizes merge.

Useful commands on user Windows:
```powershell
cd C:\Projects\MiniAV
git switch feature/m3-clamav-adapter
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1
dotnet run --project .\tests\MiniAV.ClamAV.Regression\MiniAV.ClamAV.Regression.csproj -c Release -- --native-io-test
```

## Documentation
- `docs/M3_NATIVE_LAUNCHER_PLAN.md`: native launcher design, handle and cleanup contract (some earlier sections say prototype pending; user PASS is recorded above).
- `docs/M3_WINDOWS_ACCEPTANCE.md`: chronological detailed local validation and limits.
- `docs/M3_CLAMAV_ADAPTER_DESIGN.md`, `docs/02_ARCHITECTURE.md`, `docs/03_SECURITY_MODEL.md`, `docs/05_DECISIONS.md`, `docs/06_ENGINEERING.md`: earlier decisions and architecture.

## New-session starter (paste this)
> 继续 MiniAV 项目。请从 GitHub 私有仓库 `richardlou12717/MiniAV` 的 **`feature/m3-clamav-adapter` 分支**读取最新 `PROJECT_STATUS.md`、`docs/M3_NATIVE_LAUNCHER_PLAN.md` 和相关代码。M3 原生 `STARTUPINFOEX` stdout/stderr 原型在 Windows 本机已通过 `--native-io-test`；现在继续 M3.1 生产级原生启动器的取消、超时与资源清理设计和实现。不要合并 Draft PR #3；保持 `main` 稳定。
