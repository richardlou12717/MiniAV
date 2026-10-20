# MiniAV — Project Status / Cross-session Handoff

**Updated:** 2026-10-19  
**Active milestone:** M3 — optional ClamAV out-of-process adapter; production native-launch hardening in progress.  
**State:** M0/M1/M2 merged into stable `main`. M3 remains **unmerged** on `feature/m3-clamav-adapter`, [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) (confirmed open, draft, unmerged on 2026-10-19). **Do not merge without explicit user authorization.**

## CURRENT HANDOFF — read this section first (2026-10-19)

**Current decision:** M3.1 isolated native launcher is extensively tested but **NOT production-ready**. Do not merge [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) or alter `main`; do not replace `ClamProcessRunner` until the remaining blockers are resolved.

### What exists in this branch
- `WindowsSuspendedLauncher.cs` (isolated): `CreateProcessW(CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT)`, `STARTUPINFOEX` explicit three-handle stdio allowlist, `ClamJob.Attach` before `ResumeThread`, bounded stdout/stderr, cancellation and deadline, kill-on-close Job and native process cleanup.
- `WindowsChildPipe.Create` uses synchronous Win32 `CreatePipe`. **Keep FileStream `isAsync:false`**: trying `isAsync:true` caused an observed `ArgumentException` (handle not opened for overlapped I/O). The later bounded reader-join is **not proof** of deterministic cancellation/quiescence of a synchronous-backed pipe read.
- Cleanup now closes Job and *waits for exit first*; only after a timeout attempts `TerminateProcess`, then confirms termination. Immediate TerminateProcess after Job close previously caused an observed Win32 error 5; the corrected ordering passed Windows tests. Native wait/terminate failures are reported as `IOException` (except remaining edge cases discussed below). Production scanner still uses `Process.Start` followed by Job assignment — **execution-before-Job race remains**.
- Regression files: `NativeLauncherRegression.cs`, `NativeDescendantFailureRegression.cs`, `NativeFailureRepeatRegression.cs`, `NativeInheritedIoRegression.cs`, plus earlier suspended launch/pipe/Job tests. The new M3.1 tests are opt-in, not a substitute for the ordinary regression script.

### User-confirmed Windows acceptance
- `--native-io-test`: native redirected stdout/stderr with Job-before-resume — PASS.
- `--native-launcher-test`: exit/output, cancellation, timeout, per-stream output limit — PASS **after** wait-before-terminate cleanup correction.
- `--native-launcher-repeat-test`: 30 ordinary launches, warmed-up handle delta **+4**, PASS (early un-warmed +50 failure must not be erased).
- `--native-pre-resume-failure-test`: injected failure after Job attachment before resume, no marker and no live suspended child — PASS earlier; **not rerun** in latest batch.
- `--native-active-io-cancel-test`: 5 cancellations during ongoing-output fixture — PASS after restoring synchronous stream mode.
- `--native-descendant-cancel-test`: cancellation removes sleeping descendant — PASS.
- `--native-descendant-failure-test`: timeout and stdout/stderr overflow remove descendant — PASS, including after latest cleanup sequencing.
- `--native-failure-repeat-test`: 30 alternating cancellation/timeout/overflow runs, baseline **267** handles, +5 at run 10, +6 at run 20, **+8 at run 30**, PASS; slow handle growth is *not* ruled out.
- `scripts/regression-test.ps1`: latest user-observed **PASS** after cleanup sequencing fix, Release build 1.9s; Core, quoting, pipe transfer, suspended-launch, Job, ClamAV subprocess and fake-runner checks. The script does not necessarily run every optional M3.1 command.
- Latest GitHub Windows CI lookup for earlier head `86018be`: **in_progress**; no green CI conclusion established for final handoff commits.

### Current-session development — overlapped pipe feasibility (2026-10-19)

- Added isolated `WindowsOverlappedOutputPipe.cs` prototype using Windows current-user named pipes with an asynchronous parent server reader and inheritable child writer. Added `NativeOverlappedPipeRegression.cs`, wired to opt-in `--native-overlapped-pipe-test`.
- **Unverified on Windows:** no local PASS claimed, and no production launcher integration. Existing `WindowsSuspendedLauncher` still uses synchronous `CreatePipe` outputs and retains its reader-lifetime blocker. The existing ordinary regression suite is unchanged.
- Next first command (after pull): `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-pipe-test`. Report failures verbatim, especially construction/connection, inheritance, and cancellation. If PASS, build a suspended-child end-to-end fixture before switching native launcher streams.
- Still required: reader task quiescence under cancellation/timeout/overflow, process/Job and stream cleanup error injection, longer failure-repeat diagnostics, Windows CI confirmation. Keep PR #3 Draft, do not merge main.

### M3.1 overlapped pipe Windows acceptance (2026-10-19)

- User reran opt-in `--native-overlapped-pipe-test` after commit `e4d026b` (read is scheduled before write) and reported **PASS**.
- Earlier first attempt hung without output, then instrumented attempt reached `M3.1 pipe stage: write` and timed out. The fixture now starts the read before the write, and the user reports success.
- This PASS covers a single in-process named-pipe byte transfer and cancellation of a pending asynchronous parent read while the writer remains open. It is NOT a suspended native child/STARTUPINFOEX/Job test and does NOT prove entire launcher or descendant cleanup.
- Next: build an **isolated** end-to-end STARTUPINFOEX suspended child fixture with two independently connected overlapped stdout/stderr pipes, explicit inherited-handle allowlist, Job-before-resume, and cancellation/timeout/flood/retained-descendant-writer cases. Verify reader task completion before disposing streams. Preserve original/cleanup failures separately. Leave production runner untouched and PR #3 Draft.

### M3.1 opt-in overlapped native launcher smoke pending Windows test (2026-10-19)

- Added optional `useOverlappedOutputPipes` argument to **isolated** `WindowsSuspendedLauncher.RunAsync`; default remains false and existing synchronous-output paths are left unchanged. Opt-in path uses two distinct `WindowsOverlappedOutputPipe` instances as stdout and stderr with the existing three-handle `STARTUPINFOEX` allowlist, suspended creation, Job attachment before resume and bounded readers.
- Added `--native-overlapped-launcher-test` opt-in test to run harmless PowerShell via suspended native launch and assert stdout, stderr and exit 2. **Windows result pending**; do not claim PASS. Existing `--native-overlapped-pipe-test` previously passed on user Windows.
- Run: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-launcher-test`.
- The new flag is a preliminary feasibility path, NOT proven deterministic reader quiescence. Fault-path cancel/timeout/overflow, native cleanup error propagation, inherited descendant writer handles and long handle trend remain blockers. In particular, any bounded reader join timeout and concurrent stream disposal must be redesigned before production migration. Do not run full pressure tests before basic Windows smoke succeeds. Keep PR #3 Draft, production runner and main unchanged.

### M3.1 suspended overlapped launcher Windows smoke PASS (2026-10-19)

- User ran `--native-overlapped-launcher-test` after the opt-in implementation and reported **PASS**. This exercises a harmless PowerShell process created suspended, assigned to a Job before resume, with independent overlapped stdout/stderr named-pipe readers; expected stdout, stderr and exit code were asserted.
- This is a **basic successful-exit fixture only**. The default synchronous pipe path and production `ClamProcessRunner` remain unchanged.
- Next gates: instrument both reader tasks and confirm terminal states before stream disposal on cancellation, timeout, overflow, including descendant-retained writer endpoints; then inject cleanup errors and run sustained failure-path/handle diagnostics. Do not infer deterministic shutdown, leak freedom or real ClamAV compatibility from this smoke. Keep PR #3 Draft and main untouched.

### M3.1 overlapped failure-path reader terminal test (2026-10-19; pending Windows validation)

- The isolated `WindowsSuspendedLauncher` cleanup now explicitly checks that **both** stdout/stderr reader tasks are terminal after the bounded join, rather than accepting the first `Task.WhenAll` failure as proof of quiescence. A test-only `readersQuiescedTestHook` runs before reader/stream disposal. Bounded timeout still fails closed and may leave unresolved in-flight I/O; this is not yet a comprehensive disposal solution.
- Added opt-in `--native-overlapped-failure-test` for cancellation with active output, execution timeout, and concurrent output overflow. Each scenario asserts both reader tasks terminal in the cleanup hook; suite additionally requires all three hooks to run.
- Windows test **NOT YET EXECUTED**. Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-failure-test`. If it fails or hangs, capture last stage/error; do not regard failure as merely flaky.
- Existing smoke `--native-overlapped-launcher-test` passed on user's Windows previously; production runner remains unchanged. Additional outstanding: deterministic handling if reader join times out, retained descendant writer endpoints, native wait/terminate injected failures and exception preservation, longer handle diagnostics and final CI. PR #3 Draft; do not merge main.

### M3.1 overlapped failure-path Windows acceptance — 2026-10-19

- User ran `--native-overlapped-failure-test` after commit `fa5e9b0` and reported **PASS**. Three isolated native launcher scenarios (active-output cancellation, timeout, simultaneous stdout/stderr overflow) completed with the test-only reader quiescence checks; both stdout/stderr reader tasks were observed terminal before stream disposal in these runs.
- This supplements earlier PASS for in-process named-pipe cancellation and suspended launcher stdout/stderr/exit smoke. It does **not** establish universal cancellation/disposal safety if an I/O operation fails to quiesce within the bounded join, or freedom from long-term handle leaks.
- Next: retained/blocked descendant writer and deliberately adversarial I/O tests; explicitly handle failed reader joins without disposing while reads remain in flight; native wait/terminate fault injection and original-error retention; sustained exceptional-path handle measurements; Windows CI.
- Production `ClamProcessRunner` remains untouched. Keep PR #3 Draft; no main merge.

### M3.2 adversarial descendant writer opt-in fixture — pending Windows validation (2026-10-19)

- Added `--native-overlapped-retained-writer-test`, an opt-in Windows-only fixture. A harmless suspended PowerShell launcher parent starts a sleeping PowerShell descendant using `Start-Process -NoNewWindow`, writes its PID to a random temporary marker, then the test cancels the launcher. Assertions: cancellation propagated; reader-quiescence hook was invoked with both readers terminal; marked descendant is no longer alive. Defensive fixture cleanup kills any survivor; that cleanup is not evidence of native launcher success.
- Run after pulling: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-retained-writer-test`. **No Windows PASS yet.** Capture failure stage and exception. No malware files or real ClamAV used.
- Limitation: `Start-Process -NoNewWindow` does not independently prove stdout/stderr handles were retained by the descendant; need an explicit inherited-handle/EOF assertion before claiming that particular adversarial condition. Further: timeout/overflow descendants, error-injection cleanup and long stress.
- Remain on feature branch. PR #3 Draft; no production runner or main changes.

### M3.2 retained-writer descendant fixture Windows acceptance (2026-10-19)

- User executed `--native-overlapped-retained-writer-test` and reported **PASS** after fixture commit `097fb7d`. The fixture observed a descendant PID, cancelled the isolated overlapped native launcher, saw the reader-quiescence hook report both tasks terminal, and verified no living marked descendant remained before defensive cleanup.
- **Evidence limit:** the current PowerShell `Start-Process -NoNewWindow` fixture does not independently establish that descendant stdout/stderr writer handles remained open after the parent exited. Therefore record PASS for the tested cancellation/descendant cleanup sequence, *not* proof of the stronger inherited-writer EOF adversarial case.
- Next: implement a handshake/hold-open fixture that independently verifies retained writer endpoints and parent exit before cancellation; cover corresponding timeout/overflow paths, injected native cleanup errors and exception preservation, sustained handle trends, and latest CI. Do not replace production `ClamProcessRunner` yet. PR #3 stays Draft; main untouched.

### M3.2 strengthened retained-writer evidence gate (2026-10-19; Windows test pending)

- Added a **test-only** native launcher callback when the direct child process wait completes; it reports whether stdout and stderr reader tasks are still pending at that moment. The retained-writer fixture now waits for a marked descendant PID **and** for the direct parent to exit with **both** readers still pending, before issuing cancellation. It then requires the normal reader-quiescence callback and the marked descendant's termination.
- This upgrades the previously weaker test to detect an actual post-parent-exit unclosed-output condition, rather than assuming that `Start-Process -NoNewWindow` implies inheritance. The exact descendant handle identities are still not independently interrogated; the observable EOF-blocking behavior is now explicitly asserted.
- The stronger fixture may reveal that the existing PowerShell child setup does not preserve the pipes or does not exit as expected: **do not weaken the assertion to manufacture a pass**. Use `--native-overlapped-retained-writer-test` and capture the last stage and exception. No Windows PASS has been obtained for this revision.
- Production runner unchanged; PR #3 Draft and main untouched. Remaining: failed-reader-join disposal safety, cleanup fault injection, longer handle diagnostics, CI.

### M3.2 strengthened parent-exit / pending-readers Windows PASS (2026-10-19)

- User reported **PASS** for the strengthened `--native-overlapped-retained-writer-test` after commit `cd2921e`. The fixture requires a descendant PID marker, observes the direct parent's process-wait completion **while both stdout and stderr reader tasks remain pending**, then requests cancellation. It requires both reader tasks to become terminal before stream disposal and verifies the marked descendant exited before defensive fixture cleanup.
- This establishes the observable adversarial behavior (parent exited while output pipes did not reach EOF) and cleanup for this tested execution. It does not independently inspect the descendant's Windows handle table, establish all handle-inheritance variants, guarantee reader quiescence on failed join, or prove leak freedom.
- Next gates: explicitly protect stream ownership if reader joining fails; native wait/terminate and cleanup fault injection with original exception preserved; longer stress/handle trend, nested Job policy, CI, then production integration only after authorization and evidence.
- `ClamProcessRunner` remains unchanged. PR #3 stays Draft; do not merge main.

### M3.2 original-plus-cleanup failure propagation gate (2026-10-19; Windows test pending)

- Updated isolated `WindowsSuspendedLauncher` to capture original operation exceptions using `ExceptionDispatchInfo`. If native cleanup also fails, an `AggregateException` now preserves the original exception and an `IOException` wrapping the cleanup failure. If only the original fails, it is rethrown with its stack. The test-only `cleanupFailureTestHook` injects a fault **after** genuine native process exit has been confirmed.
- Added opt-in `--native-cleanup-double-fault-test`: throws at pre-resume after Job assignment, injects a second cleanup failure, asserts both causes in order and verifies that the suspended PID did not survive. **Not yet Windows-validated.**
- Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-cleanup-double-fault-test`, followed by previously-passing `--native-overlapped-failure-test` and ordinary regressions if first run passes.
- This does NOT resolve the separate blocker: failed reader joins may still unwind and dispose streams while I/O is in flight. Do not integrate native launcher into production. PR #3 Draft, main unchanged.

### M3.2 double-fault and overlapped reader Windows acceptance (2026-10-19)

- User ran `--native-cleanup-double-fault-test` and received **PASS**: `M3.2 native original and cleanup exception preservation passed`. In the test-only pre-resume failure and post-confirmed-cleanup injected failure path, both exceptions were preserved and the suspended PID was not left alive.
- User subsequently reran `--native-overlapped-failure-test` and received **PASS**: active-output cancellation, timeout, and output-overflow scenarios each logged `both readers terminal before stream disposal`; final regression PASSED. These runs establish no observed regression in the tested paths.
- **Open blocker:** if the bounded reader join itself times out, current unwind can still dispose stream ownership with outstanding reads. Do not equate passing bounded fixtures with proven universal reader lifetime safety. Next: ownership-safe failed-join mechanism, deliberate injected stuck-reader testing, further cleanup failure coverage, repeated handle/resource diagnostics, CI and nested Job behavior.
- Production `ClamProcessRunner` unchanged; PR #3 Draft, main untouched.

### M3.2 deferred reader ownership prototype (2026-10-19, Windows validation pending)

- Introduced `DeferredReaderCleanup` as an **isolated, not-yet-integrated** owner for disposable reader resources and their associated tasks. A bounded join that times out fails closed immediately but **defers releasing those resources until all reader tasks complete**. If readers never complete, resources remain held: this is a fail-safe lifetime strategy, **not** a leak-free or production-ready guarantee.
- Added opt-in `--native-reader-deferred-cleanup-test`, forcing a join timeout with two incomplete tasks; completes one reader, verifies no disposal, completes second reader, then checks exactly-once disposal. No OS process involved. **No Windows PASS yet.** Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-reader-deferred-cleanup-test`.
- **Critical boundary:** This owner is not yet connected to `WindowsSuspendedLauncher`. Existing launcher code still has `using` scoped streams and pipes on failed reader join. Integrating ownership must move *all* dependent stream/pipe lifetimes out of those `using` scopes and track failures during deferred disposal, then test true blocked I/O, cancellation and fault paths. Do not claim issue resolved based on the new in-process fixture.
- PR #3 remains Draft; production runner and main untouched.

### M3.2 deferred reader ownership Windows acceptance (2026-10-19)

- User ran `--native-reader-deferred-cleanup-test` and reported **PASS** after prototype commit `de4e9c1`. The in-process fixture forced an incomplete two-reader join, verified resources were not disposed when only one reader completed, then verified exactly-once disposal after the second reader completed.
- This is a **pure ownership-model test**. It does not prove cancellation of actual Windows pipe reads, guarantee eventual cleanup of readers which never complete, or demonstrate that `WindowsSuspendedLauncher` currently avoids early stream disposal. The owner is **not integrated into the launcher**.
- Next: audit and refactor all launcher stream/pipe ownership (including nested `using` scopes) before testing deliberately stuck real readers; ensure cleanup failures remain observable, no successful scan is reported after join failure, and no production switch occurs prematurely. PR #3 remains Draft; main and production `ClamProcessRunner` unchanged.

### M3.2 deferred ownership now wired in isolated launcher (2026-10-19; **NOT WINDOWS-VALIDATED**)

- Added `DisposalTransferGate<T>` scope guards to track ownership of output pipe objects, the synchronous FileStream wrappers (when applicable), StreamReaders, and the linked read cancellation source. Replaced direct `using` disposal for these resources with an explicit `DeferredReaderCleanup` join. Once owner takes responsibility, scope guards transfer ownership; if reader joining exceeds five seconds, the owner retains the entire dependent set until its observed reader tasks terminate, and the launcher fails closed rather than reporting success.
- **Important:** No Windows build/run for this integrated code has been reported. First run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-failure-test` after `git pull --ff-only`; then `--native-overlapped-retained-writer-test`, `--native-cleanup-double-fault-test` and `scripts/regression-test.ps1`.
- Still to implement: deterministic forced *real launcher* join-timeout test, error reporting for any deferred disposal failures (current owner logs them to Debug), clean lifetime if `Job.Dispose` itself throws before owner registration, handling irrecoverably hung readers (resources remain held), long-run handle counts/CI. No production integration or main merge; PR #3 Draft.

### M3.2 integrated deferred-ownership Windows regression evidence (2026-10-19)

- After integration commit `5cfa138`, user ran `--native-overlapped-retained-writer-test` and received **PASS**: direct parent exit with stdout/stderr readers still pending, cancellation, both reader cleanup and descendant termination.
- User ran `--native-cleanup-double-fault-test` and received **PASS**: injected original and cleanup exceptions preserved.
- User ran `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1` and received **PASS**; Release build succeeded (1.8s), Core cases, Windows pipe inheritance, suspended Job-before-resume, ClamAV Job/process and fake-runner regressions passed.
- The previously requested `--native-overlapped-failure-test` was **not present in this Windows output** following integration and must be rerun; no PASS claim for that post-integration scenario. The independent `--native-reader-deferred-cleanup-test` passed before integration, not after it.
- Still unverified: *actual* reader-join timeout against launcher, deferred resource release after stalled real I/O, cleanup when Job close itself faults, long handle trend and CI. PR #3 Draft; main and production `ClamProcessRunner` untouched.

### Blocking tasks — begin here next session
1. **M3.1 reader lifetime:** guarantee both stdout/stderr reader tasks terminate before disposing their streams on cancel/timeout/overflow. `CreatePipe` handles are synchronous; do *not* switch `FileStream` to `isAsync:true` without replacing pipe creation with genuinely overlapped-capable named pipes or an explicitly validated alternative. Current 5s join may throw but still unwind to stream disposal while a read task may remain. Design, implement and instrument reader quiescence; account for inherited writer endpoints and descendants.
2. **M3.1 final cleanup:** prove Job kill, native process/thread handles, pipe and attribute-list cleanup on failure. Consider deterministic fault injection for native wait/termination failures and accurate error propagation, without orphaning process or masking original errors. Revisit pending wait task observation and scope of original exceptions.
3. **M3.2–M3.3:** longer repeated exceptional-path/handle diagnostics, deliberate blocked I/O and adversarial child/descendant cases, nested Job/denied-assignment behavior, Windows CI execution of selected opt-in fixtures. A handle-delta threshold alone is not leak proof.
4. **M3.4 (not started):** only after gates pass, integrate the native launcher into production `ClamProcessRunner` while retaining parser and CLI/WPF contracts; fail closed, no fallback to unsafe `Process.Start`.
5. **M3.5 (not started):** full regressions, real ClamAV harmless-file check, WPF selection/cancel/restart/close/manual export, and PR review. **Keep Draft until explicit merge permission**.

### Commands for next session (PowerShell, in `C:\Projects\MiniAV`)
```powershell
git switch feature/m3-clamav-adapter
git pull --ff-only
$project = ".\tests\MiniAV.ClamAV.Regression\MiniAV.ClamAV.Regression.csproj"
dotnet run --project $project -c Release -- --native-launcher-test
dotnet run --project $project -c Release -- --native-failure-repeat-test
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1
```

**New session prompt:** “继续 MiniAV 项目。读取私有仓库 `richardlou12717/MiniAV` 的 `feature/m3-clamav-adapter` 分支上最新 `PROJECT_STATUS.md` 的 CURRENT HANDOFF 及 `docs/M3_NATIVE_LAUNCHER_PLAN.md`。M3.1 的取消、超时、输出限额、子进程清理和30次异常重复测试均已有 Windows PASS，但同步 `CreatePipe` 管道的 reader task 确定性退出尚未解决。请从这一阻塞项继续，保持 PR #3 Draft，不合并 main。”

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

## M3.1 exceptional-path repeated test Windows acceptance (2026-10-19)
- User executed `--native-failure-repeat-test` and reported PASS: after warm-up baseline 265 handles; iteration 10 = 273 (+8), iteration 20 = 274 (+9), iteration 30 = 279 (+14). Thirty alternating cancellation, timeout and output-overflow runs completed, with final delta +14 beneath the fixture's +30 threshold.
- Handle growth is nonzero and continued over the last 20 iterations; this short diagnostic does **not** prove absence of a slow leak. Do not relax the threshold to dismiss that possibility. Further sustained checks and explicit reader completion/cleanup failure propagation are required before production integration.
- No additional native launcher changes in this checkpoint. Keep PR #3 Draft and `main` unchanged.

## M3.1 explicit process cleanup failure reporting (2026-10-19; Windows retest required)
- Improved outer native process cleanup: close Job first; check process wait state rather than interpreting exit code 259 as always running; terminate if still active; wait up to 5s; surface failed native termination or wait as `IOException` with the underlying Win32 failure. Handle release remains in a nested finally even on errors. Commit `1d95b16`.
- **Not yet locally validated.** Previous 30-run exceptional-path diagnostic PASS had +14 handles after warm-up and still needs longer investigation. Run `--native-launcher-test`, `--native-pre-resume-failure-test`, `--native-descendant-failure-test`, `--native-failure-repeat-test`, then `scripts/regression-test.ps1`. Do not infer reader task quiescence from success alone.
- Remaining risk: synchronous `CreatePipe` reader-task cancellation may be non-deterministic. The inner bounded 5s join can report IOException but still disposes stream after it; must resolve before calling the launcher production-grade. ClamProcessRunner unchanged; PR #3 remains Draft.

## M3.1 cleanup race correction (2026-10-19; awaiting Windows retest)
- User's `--native-launcher-test` failed after cleanup-reporting change: `IOException` wrapping Win32 error 5 `Native child termination failed`. Likely race between asynchronous kill-on-Job-close and immediate fallback `TerminateProcess`; cause not yet confirmed.
- Changed cleanup order: close Job; wait for native process termination (5s); only on wait timeout call `TerminateProcess`; if fallback returns error, accept a concurrent exit only after successful process-signaled confirmation. Otherwise fail closed. Commit `515c11a`.
- Re-run the single `--native-launcher-test` first. Other tests and production integration must wait until this regression is resolved. PR #3 Draft, `main` unchanged.

## M3.1 cleanup wait-before-terminate Windows acceptance (2026-10-19)
- User reports `--native-launcher-test` PASS after commit `515c11a` changed cleanup to wait for Job kill completion before fallback TerminateProcess. This resolves the observed error 5 in the basic regression fixture, but does not establish that all cleanup races are eliminated.
- Next run pre-resume failure, timeout/output-overflow descendants, exceptional-path repetition, and full regression with the new cleanup sequencing. Retain the synchronous pipe reader quiescence issue as production-integration blocker. PR #3 Draft and `main` unchanged.

## M3.1 latest Windows acceptance (2026-10-19)
- User confirmed native descendant timeout and output-overflow test PASS, exceptional repeat test PASS (baseline 267 handles; at 10=272 +5, 20=273 +6, 30=275 +8), and full `scripts/regression-test.ps1` PASS (Release build in 1.9 s, Core and ClamAV and Windows pipe/suspended-launch tests).
- Earlier `--native-launcher-test` PASS after Job close/wait sequencing correction. The latest user message did not include `--native-pre-resume-failure-test` output, so do not record a new pass for that test; it passed in an earlier round.
- Residual risk: cancellation semantics and reader quiescence of synchronous `CreatePipe` reads; bounded join alone is not a proof that tasks stop before stream disposal. Handle growth +8 is below diagnostic limit but not proof of absence of slow leaks.
- Do not integrate native launcher into production `ClamProcessRunner` until robust pipe shutdown and cleanup error handling are verified. PR #3 remains Draft; main unchanged.

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
