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

## M3.1 repeat-test failure and investigation (2026-10-19)
- User ran the 30-iteration `--native-launcher-repeat-test`: handles before=204, after=254, delta=+50; test correctly FAILED. Treat as suspected resource leak, not yet root-caused.
- Changed launcher cancellation watcher from an infinite `Task.Delay` to a `TaskCompletionSource` and explicitly disposed `CancellationTokenRegistration` (commit `3677dca`). This is a suspected lifetime issue, **not proven as the +50 root cause**.
- Updated repeated-run fixture to perform three warm-up runs and print handle counts every ten iterations (commit `31fadef`). Await local retest and check growth slope. If growth persists, investigate handle types and async Process/pipe lifetimes; do not weaken the guardrail or connect launcher to production.
- Prior single-run native smoke PASSED. Production `ClamProcessRunner` still unchanged. PR #3 remains Draft, no merge.

## M3.1 repeat-test Windows validation (2026-10-19)
- User re-ran `--native-launcher-repeat-test` after cancellation-registration disposal and warm-up improvements: run 10 handles=256 (+2), run 20=257 (+3), run 30=258 (+4); baseline=254, final=258, delta=+4. Test PASS.
- Compared with earlier un-warmed +50, the new result shows no comparable rapid growth over 30 launches, but different warm-up and implementation mean it does not conclusively identify the earlier root cause or prove leak freedom. Require longer/failure-path/descendant checks before production integration.
- `ClamProcessRunner` is still unchanged; keep PR #3 Draft and `main` stable.

## M3.1 full regression acceptance (2026-10-19)
- User ran `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1` on Windows and reported PASS (Release build, Core cancellation/read-error/limits/window lifecycle, Windows argument quoting and pipe inheritance/byte transfer, suspended Job-before-resume and failure cleanup, ClamAV Job membership, real subprocess and fake runner). Build succeeded in 2.3s.
- Combined with the opt-in 30-run diagnostic (+4 handles after warm-up), M3.1 baseline is stable enough to begin targeted failure-path regressions, **not** production integration. The native launcher remains isolated from production `ClamProcessRunner`.
- Remaining: native cancellation/timeout child-descendant termination assertions, injected failure before resume, reading-task shutdown after overflow, repeated exceptional paths and hard cleanup reporting. Draft PR #3 only.

## M3.1 pre-resume fault injection (2026-10-19; awaiting Windows verification)
- Added an internal `beforeResumeTestHook` to the isolated `WindowsSuspendedLauncher`, invoked only after successful Job attachment and before `ResumeThread`; it permits deterministic failure injection. Production ClamAV adapter and ordinary callers do not use this hook.
- New opt-in command: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-pre-resume-failure-test`.
- Harmless PowerShell marker-writing child is created suspended, the hook throws, and the test checks that the marker was never created and the PID is not alive afterward. Not yet tested on user's Windows machine; the outcome must not be called PASS until executed.
- Subsequent required work: cleanup failure visibility, in-flight pipe-read lifecycle, descendant/orphan tests, exceptional repeated-run stress, full regression. PR #3 remains Draft; no production integration.

## M3.1 pre-resume failure Windows acceptance (2026-10-19)
- User ran `--native-pre-resume-failure-test` on Windows; output: `M3.1 pre-resume fault injection and suspended-child cleanup passed`.
- This validates one deterministic path after Job attachment and before ResumeThread: no marker file and no remaining live child at assertion. Does not prove cleanup of descendants or concurrent pipe readers.
- Next: exercise cancellation/timeout while output is active, then descendant/job cleanup, repeated exception paths, and explicit cleanup failure handling. Keep PR #3 Draft and production runner unchanged.

## M3.1 active pipe cancellation regression (2026-10-19; pending local validation)
- Added opt-in `--native-active-io-cancel-test`: five harmless PowerShell runs that attempt continuous concurrent stdout/stderr writing while cancellation fires, each guarded against a fixture hang.
- This is a first active-I/O cancellation test, not yet proof that both readers were busy when cancelled. Need stronger deterministic synchronization, timeout/overflow cleanup, and descendant checks.
- Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-active-io-cancel-test` locally. Production runner remains unchanged.

## M3.1 active I/O cancellation Windows acceptance (2026-10-19)
- User ran `--native-active-io-cancel-test` and reported: `M3.1 active stdout/stderr cancellation regression passed`. Five repetitions completed without guard timeout or unexpected output-overflow error.
- This does not prove both pipes were actively draining at the exact cancellation instant, that reader tasks have all quiesced, or that grandchildren are terminated; targeted instrumentation remains needed.
- Next implement a descendant PID-marker fixture for cancellation, timeout, and output overflow, then verify no surviving child processes. Keep production runner unchanged and PR #3 Draft.

## M3.1 descendant cleanup fixture (2026-10-19; not yet validated)
- Added opt-in `--native-descendant-cancel-test`: a harmless PowerShell parent spawns a sleeping PowerShell child and writes its PID to a temporary marker; the fixture waits for that marker, cancels native launch, then verifies the descendant exits after Job close. Defensive test cleanup kills a survivor and fails the assertion; it is not evidence of launcher success.
- This test is Windows-only and **has not yet been run**. A PASS would cover one parent/descendant cancellation case only; timeout, output overflow, concurrent pipe read quiescence and cleanup failure reporting still need focused testing. Do not integrate production runner.
- Run: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-descendant-cancel-test`.

## M3.1 descendant cancellation Windows acceptance (2026-10-19)
- User ran `--native-descendant-cancel-test` on Windows and observed `M3.1 cancellation kills descendant Job members passed`. The isolated native launcher terminated the sleeping descendant in this cancellation fixture.
- This validates one descendant cancellation scenario only; timeout/output-limit descendant cleanup, concurrent pipe reader shutdown, sustained handle use and cleanup failure handling remain pending. Production `ClamProcessRunner` remains unchanged. PR #3 stays Draft.

## M3.1 native descendant timeout/output-overflow tests (2026-10-19; awaiting local Windows validation)
- New opt-in `--native-descendant-failure-test` runs two harmless PowerShell parent/grandchild fixtures. For timeout, a sleeping descendant must be alive before a short native execution deadline; for output overflow, a descendant marker is written before the parent floods both stdout/stderr beyond 1,024 chars. Both assert the expected exception and verify descendant termination, with defensive test cleanup only on failure.
- To validate: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-descendant-failure-test`.
- **Not yet tested on Windows.** Further work remains on in-flight pipe reader shutdown, explicit cleanup failure reporting, repeated exceptional stress, and production integration. ClamProcessRunner unchanged; PR #3 Draft and main untouched.

## M3.1 timeout and output-overflow descendant Windows acceptance (2026-10-19)
- User ran `--native-descendant-failure-test` and observed all three PASS lines: timeout descendant cleanup, output-overflow descendant cleanup, and combined fixture.
- Alongside earlier cancellation descendant PASS, the three primary failure causes now have isolated Windows fixture coverage showing no surviving sleeping descendant after launcher return. This is not proof of exhaustive process-tree cleanup in every environment.
- Next: guarantee concurrent stdout/stderr readers terminate before disposing their streams, surface cleanup failures deterministically, and add exceptional-path repeat diagnostics. Do not switch production `ClamProcessRunner` until these pass. Keep PR #3 Draft and `main` unchanged.

## M3.1 pipe-reader cleanup hardening (2026-10-19; pending Windows validation)
- Updated isolated `WindowsSuspendedLauncher` to open parent read handles with async FileStreams and explicitly join both stdout/stderr read tasks after stopping reads and closing the Job. A bounded five-second reader join reports `IOException` rather than silently treating an unresolved drain as success.
- Previous tests passed **before** this change; the new behavior has not yet been validated on Windows. Run `--native-launcher-test`, `--native-active-io-cancel-test`, `--native-descendant-failure-test`, and full `scripts/regression-test.ps1`. Watch for hangs, unexpected IOException, and exception masking.
- Still outstanding: rigorous cleanup failure propagation (including native WaitForSingleObject failure), repeated exceptional paths, independent read-task observation, and final production integration. No change to production `ClamProcessRunner`; PR #3 Draft.

## M3.1 Windows pipe handle regression and correction (2026-10-19)
- User executed `--native-launcher-test` after async stream change and observed `ArgumentException: Handle does not support asynchronous operations` at `WindowsSuspendedLauncher.cs:87`. Root cause: `WindowsChildPipe.Create` uses Win32 `CreatePipe` (synchronous, not overlapped), so `FileStream(isAsync:true)` cannot wrap its parent handle.
- Corrected both stdout and stderr FileStream constructors back to `isAsync:false` (commit `16df1e1`). Reader task join remains experimental and **must be revalidated**. Synchronous-backed pipe reads can still have cancellation/shutdown edge cases; do not claim deterministic reader quiescence from this correction.
- Re-run `--native-launcher-test`, `--native-active-io-cancel-test`, `--native-descendant-failure-test` on Windows, followed by full regression. Do not integrate into production until cleanly verified. PR #3 stays Draft.

## M3.1 post-correction Windows acceptance (2026-10-19)
- User reports all three requested opt-in commands PASS after restoring `FileStream(isAsync:false)` for synchronous `CreatePipe` handles: `--native-launcher-test`, `--native-active-io-cancel-test`, and `--native-descendant-failure-test`. No logs beyond the PASS report were supplied.
- This revalidates normal native launch, active I/O cancellation, and descendant cleanup on timeout/overflow after the correction. It does **not** establish deterministic cancellation of outstanding synchronous-backed pipe read tasks under every race, nor confirm a fresh full `regression-test.ps1` pass after this latest change.
- Before production integration: explicitly verify reader quiescence, cleanup failure propagation, exceptional-path repetition, Windows CI and final full regression. Keep PR #3 Draft; `ClamProcessRunner` remains unchanged.

## M3.1 repeated exceptional-path diagnostic (2026-10-19, awaiting Windows validation)
- Added opt-in `--native-failure-repeat-test`, 30 alternating harmless Windows launches covering user cancellation, timeout and simultaneous stdout/stderr overflow, with 3 warm-up runs and a bounded watchdog per launch. Measures process handle-count deltas at 10/20/30 runs and fails on unexpected exception or >30-handle growth after warm-up.
- This is diagnostic only; no proof of leak freedom or deterministic synchronous-pipe reader shutdown. **Not yet locally validated.** Next: run new fixture and full regression, then address cleanup failure reporting and unresolved in-flight synchronous reader safety before replacing production runner.
- Test command: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-failure-repeat-test`. PR #3 remains Draft, main unaffected.

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
