# MiniAV — Project Status / Cross-session Handoff

**Updated:** 2026-10-20  
**Active milestone:** M3 — optional ClamAV out-of-process adapter; production native-launch hardening in progress.  
**State:** M0/M1/M2 merged into stable `main`. M3 remains **unmerged** on `feature/m3-clamav-adapter`, [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) (confirmed open, draft, unmerged on 2026-10-19). **Do not merge without explicit user authorization.**

## M3.3 Job consumed-ownership Windows acceptance — 2026-10-24
- User reported **PASS for all four requested Windows commands** after ownership fix `aa8dfb0`: `scripts/regression-test.ps1`, `--native-job-close-error-test`, `--native-job-external-close-test`, and `--native-timeout-cleanup-double-fault-test`. No verbatim per-test output was included.
- This accepts the launcher ownership fix in the tested scenarios. The external-close test starts with a *successful* initial close followed by stale-handle error; it does **not** establish that a genuinely failed first close of the last valid Job handle terminates descendants.
- Remaining M3.3 blockers: failed first Job close descendant certainty, permanently stalled kernel-I/O/reader lifetime, and fresh production-integration acceptance. Do not switch `ClamProcessRunner` or merge Draft PR #3; main remains stable.

## M3.3 Job close ownership race fix — 2026-10-24 (WINDOWS PENDING)
- Code review exposed a double-close hazard in the *isolated* `WindowsSuspendedLauncher` inner cleanup: `job = null` happened only after `job.Dispose()` returned, so a throwing native Job `CloseHandle` left the already-consumed `ClamJob` wrapper reachable by the outer finally. While `ClamJob.Dispose` itself is idempotent, the launcher owner state should be cleared **before** native disposal.
- Commit `aa8dfb0` transfers ownership into a local and clears `job` before `Dispose`; original cleanup exceptions remain observable and outer cleanup cannot try to act on a consumed Job wrapper. This does **not** prove descendant termination on failed Job close.
- Change pending Windows acceptance. Run full regression, `--native-job-close-error-test`, `--native-job-external-close-test`, and `--native-timeout-cleanup-double-fault-test`. No production Runner or main change; PR #3 Draft.

## M3.3 native raw handle cleanup Windows acceptance — 2026-10-21
- User confirmed **all three requested Windows commands PASS** after commit `87797ab`: `scripts/regression-test.ps1`, `--native-timeout-cleanup-double-fault-test`, and `--native-adapter-compatibility-test`. No verbatim per-test output was provided.
- This validates normal full regression and previously covered fault-aggregation/adapter behavior after new process/thread `CloseHandle` error reporting. It does **not** directly force a failed real native process/thread handle close.
- Remaining M3.3 P0 safety boundaries: first valid Job handle close failure cannot be assumed to kill Job descendants; a never-completing native overlapped read can retain deferred resources indefinitely. Any design must fail closed and avoid claiming cleanup verification where OS termination is unconfirmed.
- Production `ClamProcessRunner` and main unchanged; Draft PR #3 remains Draft/unmerged.

## M3.3 native process/thread handle-close error reporting — 2026-10-21 (WINDOWS PENDING)
- Commit `88b63e4` updates isolated `WindowsSuspendedLauncher` outer cleanup to check Win32 `CloseHandle` for process/thread handles, capture `GetLastWin32Error` immediately and retain multiple native cleanup faults alongside the original operation error. It continues closing remaining handles after one close fails and observes `child.Dispose()` failure rather than allowing it to overwrite the first error.
- This is isolated launcher code only; production Runner and main are not modified. Windows build and regressions after this change **have not yet been run**. Run `scripts/regression-test.ps1`, then opt-in `--native-timeout-cleanup-double-fault-test` and `--native-adapter-compatibility-test`.
- This does not establish that a failing valid Job close terminates descendants, nor guarantee indefinitely blocked kernel read completion. Draft PR #3 stays Draft/unmerged.

## M3.3 October 21 CI confirmation and release gate
- Rechecked GitHub Actions on 2026-10-21: **Windows CI SUCCESS** for feature branch HEAD `44905ef8c358db6e2e1ca4dde2a688320476bc56` (two completed successful Windows CI runs). Draft PR #3 remains open/draft/unmerged. This supersedes the previous in-progress CI snapshot.
- Previously user-confirmed Windows PASS: `scripts/regression-test.ps1` and opt-in real harmless `--native-real-clam-smoke-test`. These are separate from CI and do not verify malware coverage.
- M3.3 readiness assessment: isolated adapter contract and tested native failure paths are encouraging but **do not authorize replacing the production `ClamProcessRunner`**. Outstanding: a genuine first valid Job `CloseHandle` failure leaves descendant termination unconfirmed; deferred owner keeps reader resources until terminal completion but cannot guarantee permanently stalled kernel I/O settles; native launcher thread/process `CloseHandle` return values are not all checked; post-integration WPF/CLI and CI acceptance not yet run.
- Preserve `main`, production Runner and Draft PR #3. Next work should target observable native thread/process close errors without masking the primary failure, and a fail-closed recovery strategy for an unconfirmed Job kill.

## M3.3 full regression + real native ClamAV Windows acceptance — 2026-10-20
- User replied “通过” to the two requested Windows checks: `scripts/regression-test.ps1` and opt-in `--native-real-clam-smoke-test "C:\\Program Files\\ClamAV\\clamscan.exe"`. Record **user-reported PASS for both**, but no detailed stdout, installed database version, or per-test log was supplied.
- The real native smoke runs a harmless file through `ClamAvScanEngine` with explicit `NativeClamProcessRunnerPrototype`, without switching default production Runner. It establishes one real ClamAV NoMatch path, **not** malware detection coverage, database freshness or broader environment compatibility.
- Remaining M3.3 blockers: current-head CI confirmation, cleanup guarantees on actual first Job CloseHandle failure, indefinite overlapped reader/kernel-I/O edge cases, and eventual separate WPF/CLI production-integration acceptance. Draft PR #3 stays Draft and main stable.

## M3.3 opt-in real ClamAV native smoke — 2026-10-20 (WINDOWS PENDING)
- New `NativeRealClamSmokeRegression` and `--native-real-clam-smoke-test <absolute-clamscan.exe> [absolute-database-directory]` use a harmless temp text file, existing `ClamAvScanEngine` parsing/argument generation, and explicit `NativeClamProcessRunnerPrototype` injection. It requires `ScanStatus.NoMatch`; any engine error, missing DB, unexpected stdout/stderr, cleanup fault or incomplete scan fails closed.
- This **does not** change CLI/WPF or the default `ClamProcessRunner`. The normal `scripts/clamav-local-smoke.ps1` still exercises the existing production path.
- Windows test **not yet run**. Example: `dotnet run --project $project -c Release -- --native-real-clam-smoke-test "C:\\Program Files\\ClamAV\\clamscan.exe"` (optionally append absolute database directory). This test does not verify signatures' freshness or exhaustive malware coverage.
- Following user PASS for `--native-adapter-compatibility-test`, a new full `scripts/regression-test.ps1` PASS remains unconfirmed. Run full regression and this opt-in real smoke before considering further integration. Keep PR #3 Draft/unmerged and main stable.

## M3.3 native adapter compatibility Windows acceptance — 2026-10-20
- User reported **PASS** for opt-in `--native-adapter-compatibility-test` after commit `473cad1`. The fixture exercises exits 0/1/2 with stdout/stderr, native output-cap translation to `ProcessOutputLimitException`, deadline timeout and caller cancellation via `NativeClamProcessRunnerPrototype`.
- User's brief PASS does **not establish a new full `scripts/regression-test.ps1` run**, real `clamscan.exe` compatibility, or WPF acceptance. Do not claim these passed.
- Next Windows step: run full `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1`; then review real ClamAV harmless smoke via isolated opt-in adapter, while preserving current production Runner/main and Draft PR #3.

## M3.3 isolated native adapter compatibility prototype — 2026-10-20 (WINDOWS PENDING)
- Introduced internal `NativeClamProcessRunnerPrototype : IClamProcessRunner` as an **opt-in-only** compatibility boundary. It calls the isolated suspended launcher with `useOverlappedOutputPipes: true`, existing production-equivalent limits (32,768 chars/stream and 2 GiB Job memory), and returns `ProcessExecution`. `ClamAvScanEngine` still defaults to the original `ClamProcessRunner`.
- Native `InvalidDataException` with the exact native output-cap message is translated into production `ProcessOutputLimitException`. Other InvalidData/IO/aggregate cleanup faults are not rewritten or hidden. Production default Runner/exception contracts remain unchanged.
- New opt-in Windows `--native-adapter-compatibility-test` covers harmless PowerShell exit codes 0/1/2 with both stdout/stderr, two-stream overflow translation, deadline and caller cancellation. **Not yet run on Windows; no PASS asserted.**
- Run after pull: `dotnet run --project $project -c Release -- --native-adapter-compatibility-test`. Then full `scripts/regression-test.ps1` on success. This test is not proof of real `clamscan.exe` behavior or WPF acceptance; production switch still blocked. Draft PR #3 stays Draft and main stable.

## M3.3 retained-writer overflow Windows PASS — 2026-10-20
- User reported PASS for `--native-retained-writer-overflow-test` after the preceding Windows-accepted retained-writer timeout fixture. The isolated suspended launcher creates a descendant before flooding stdout/stderr; overflow is detected, both reader tasks are observed terminal and the marked descendant exits before the fixture passes.
- Together with previously accepted retained-writer cancellation and timeout fixtures, cancellation/deadline/output-limit have now been covered in targeted native overlapped scenarios. This is **not** a universal proof of I/O termination or absence of handle leaks.
- Next development should prioritize specific production-integration blockers: native output-limit exception translation, guaranteed overlapped path selection, native Job failure boundaries, and current-head CI/full regression. Do not replace production Runner or merge PR #3 without explicit authorization.

## M3.3 retained-writer timeout Windows PASS — 2026-10-20
- User executed `--native-retained-writer-timeout-test` and provided the output `M3.3 retained-writer descendant timeout and paired reader cleanup passed`.
- The isolated suspended overlapped launcher fixture observed parent exit with both output readers pending, then enforced execution deadline, reader quiescence and no remaining live marked descendant. This is a confirmed local Windows PASS for this scenario, not a general proof against permanently stuck I/O.
- Newly added `--native-retained-writer-overflow-test` remains **Windows pending**. Pull the latest branch then run that opt-in test next. No production runner switch, main unchanged, PR #3 Draft.

## M3.3 retained-writer overflow regression — 2026-10-20 (WINDOWS PENDING)
- Added opt-in `--native-retained-writer-overflow-test` for isolated suspended `WindowsSuspendedLauncher` with overlapped stdout/stderr: PowerShell parent creates a sleeping descendant, records its PID before flooding both output streams, and exceeding the 1024-character cap must throw `InvalidDataException`; both reader tasks must be terminal and the descendant exited before success.
- Defensive fixture termination only runs after failure and is not counted as success. This test does **not** require parent exit with both readers pending before overflow (the separate retained-writer timeout and cancellation fixtures cover post-parent-exit readers). Windows acceptance pending.
- Previous `--native-retained-writer-timeout-test` has **no reported PASS**; run it first, then the overflow test. Both are opt-in, production runner/main untouched; PR #3 Draft.

## M3.3 suspended retained-writer deadline regression — 2026-10-20 (WINDOWS PENDING)
- Added opt-in `--native-retained-writer-timeout-test` in `NativeOverlappedPipeRegression`: suspended PowerShell parent starts a sleeping child via `Start-Process -NoNewWindow`, exits with both output readers still pending, and the isolated overlapped launcher deadline must trigger a `TimeoutException`, quiesce both readers and terminate the observed descendant. Fixture only, no production Runner switch.
- This complements the earlier Windows-accepted retained-writer **cancellation** case with a true **deadline** route. The test requires observable parent exit with both readers pending, not just an assumed inherited writer. No Windows PASS yet.
- Run: `dotnet run --project $project -c Release -- --native-retained-writer-timeout-test`. Keep PR #3 Draft, main unchanged.

## M3.3 paired blocked stdout/stderr Windows acceptance — 2026-10-20
- User reported PASS for `--native-blocked-read-paired-test` after commit `1570cb7`. The fixture tests both real overlapped stdout/stderr reads pending while a shared DeferredReaderCleanup defers, one writer EOF not permitting premature cleanup, and final owner disposal only after both reads complete.
- This is an in-process fixture, not yet end-to-end native suspended-child/descendant handling. Further M3.3 work remains. Production Runner/main unchanged; Draft PR #3 unmerged.

## M3.3 paired real blocked-reader fixture — 2026-10-20 (WINDOWS PENDING)
- Added `--native-blocked-read-paired-test` with separate real overlapped stdout/stderr pipes held by one `DeferredReaderCleanup`. Both reads must be pending before join timeout; after stdout writer closes, stderr must remain pending and owner resources undisposed; only after stderr EOF may the owner complete disposal.
- This is an in-process two-pipe primitive fixture, not a substitute for suspended native child and actual retained-descendant behavior. No Windows PASS yet. Run `dotnet run --project $project -c Release -- --native-blocked-read-paired-test`.
- Keep M3.3 native production-integration blockers open; PR #3 Draft, main and production Runner unchanged.

## M3.3 deferred EOF/cancellation Windows PASS — 2026-10-20
- User reported **PASS for both** requested opt-in commands: `--native-blocked-read-deferred-test` and `--native-blocked-read-deferred-cancel-test`. These use real pending overlapped named-pipe reads with deferred owner retention, resolving respectively through explicit writer-close EOF and cancellation with the writer remaining connected.
- Acceptance scope: under the tested conditions, no premature disposal; reader completion followed by observable owner cleanup. No general proof of cancellation for permanently stuck kernel I/O, paired stdout/stderr readers, or suspended-child descendant behavior.
- Next step: paired simultaneous stdout/stderr pending-read ownership fixture. No production Runner integration or PR merge.

## M3.3 deferred cancellation test added — 2026-10-20 (AWAITING WINDOWS)
- New optional `--native-blocked-read-deferred-cancel-test` uses a genuine pending overlapped named-pipe read and an idle connected writer. After a bounded reader join times out and ownership is deferred, it cancels the read while the writer remains open, asserts the reader terminates as cancelled, then checks that `CleanupCompletion` finishes and the owner disposes resources.
- This complements the previously added `--native-blocked-read-deferred-test` (writer-close EOF). **Neither test has a reported Windows PASS yet**; do not infer success from the earlier `--native-blocked-read-test` acceptance.
- Run both with `dotnet run --project $project -c Release -- --native-blocked-read-deferred-test` and `dotnet run --project $project -c Release -- --native-blocked-read-deferred-cancel-test`. These are bounded in-process tests, not an OS guarantee for permanently stuck I/O.
- Draft PR #3 and main remain untouched; production Runner not replaced.

## M3.3 blocked-read Windows acceptance and deferred ownership — 2026-10-20
- User reported PASS for `--native-blocked-read-test` after commit `cbbd57b`: real overlapped read cancelled, and a second real pending read returned EOF upon writer close. No verbatim transcript provided.
- Added new isolated opt-in `--native-blocked-read-deferred-test`: a real idle-writer blocked read forces `DeferredReaderCleanup.JoinOrDeferAsync` timeout, asserts pipe ownership and disposal completion remain pending, closes writer, confirms EOF, and requires deferred completion and release. **Windows PASS not yet established**. It does not prove permanently stuck kernel I/O is cancellable, or change the production launcher.
- Next Windows command: `dotnet run --project $project -c Release -- --native-blocked-read-deferred-test`. Draft PR #3 stays Draft/unmerged, production runner and main untouched.

## M3.3 real pending overlapped read test — 2026-10-20 (WINDOWS TEST PENDING)
- New isolated opt-in `--native-blocked-read-test` checks two **real pending** asynchronous named-pipe reads with an idle connected writer: cancellation must terminate the first read, and explicitly closing the writer must deliver EOF to the second read. This is not a synthetic task gate. Test code in `NativeBlockedReadRegression.cs`, wired in regression Program.
- The fixture is intentionally in-process and does **not** prove launcher shutdown under a deliberately uncooperative descendant, indefinite OS I/O or process tree fault. Timeouts fail the test rather than being interpreted as success.
- Run after pull: `dotnet run --project $project -c Release -- --native-blocked-read-test`. On PASS, review coupling to `DeferredReaderCleanup` and extend to native suspended child without altering production Runner. PR #3 remains Draft, main unchanged.

## M3.3 external-close Windows acceptance — 2026-10-20
- User reported PASS for `--native-job-external-close-test` after commit `3f76909`. The fixture creates a live harmless Job member, successfully closes the Job handle through its test-only external-close helper, verifies later wrapper `Dispose` reports `ERROR_INVALID_HANDLE` without retrying, and verifies the member terminates.
- Scope is **successful initial Job close followed by a stale-handle close error**, not a failure to close the only valid Job handle. Native failure on the first valid close and descendant cleanup in that condition remain unproven. No per-case console transcript was supplied.
- M3.3 next: review native handle ownership and error aggregation; focus on genuine cleanup failures and reader lifecycle rather than treating synthetic post-close errors as proof. Production Runner/main unchanged, PR #3 Draft.

## M3.3 real Job member external-close diagnostic — 2026-10-20 (PENDING WINDOWS TEST)
- Added `--native-job-external-close-test` to validate a *live* harmless cmd.exe worker attached to a kill-on-close Job. A test-only helper performs a **successful external native close** of the Job handle while the wrapper retains a stale value; the subsequent wrapper Dispose must report native `ERROR_INVALID_HANDLE`, and a second Dispose must be inert. The child must exit following the successful external close.
- **Distinction:** This is not an injected failure of the *first* and only valid Job close; it does not prove descendant termination when that close genuinely fails. Such a guarantee must not be claimed.
- New regression code committed but not run on user's Windows. Run `dotnet run --project $project -c Release -- --native-job-external-close-test` first. Production runner and main remain untouched; Draft PR #3 unmerged.

## M3.3 full Windows regression acceptance — 2026-10-20
- User reported PASS after running the requested `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1` following the `--native-job-close-error-test` PASS. No verbatim log or per-case details supplied. Both focused and full regression checks are accepted for this checkpoint.
- Next work targets real child/Job close ordering and distinguishes a successful external close followed by a failing redundant close from a genuinely failed close of the last live Job handle; no claim that the latter terminates descendants.

## M3.3 Windows close-error acceptance — 2026-10-20
- User reported **PASS** after correcting the `--native-job-close-error-test` invalid-handle fixture to non-pseudo `0x12345` (commit `4e497d8`). The test exercises a real failing `CloseHandle` and verifies one-way handle ownership/no repeated close.
- User's brief “通过” does **not independently establish** full `scripts/regression-test.ps1` PASS; that remains pending. This invalid-handle fixture does not simulate a live Job close failure with descendants, so cannot establish process-tree termination under that fault.
- Next: full Windows regression; then separate live Job/descendant fault behavior and original-plus-cleanup-error preservation. Keep Draft PR #3 unmerged and production runner unchanged.

## M3.3 Windows regression correction — 2026-10-20 (NEWEST)
- User ran `--native-job-close-error-test` after commit `8235026` and it **FAILED** with `Invalid native Job handle close did not fail` at Program.cs:13. The test helper used `new IntPtr(-1)`, but -1 is the Windows current-process pseudo-handle: it was not a valid invalid-handle failure fixture.
- Corrected helper to use the intentionally unaligned non-pseudo value `0x12345` in commit `4e497d8`. This correction is **awaiting Windows rerun**. If it still fails, investigate the actual Win32 close result rather than weakening the test.
- Re-run focused `--native-job-close-error-test`, then full `scripts/regression-test.ps1`. No production runner changes; PR #3 remains Draft/unmerged, main unchanged.

## M3.3 native Job close hardening — 2026-10-20 (NEWEST)
- Commits: `44ae55b` checks the real Win32 `CloseHandle` result in `ClamJob.Dispose`, captures the Win32 error and consumes ownership before closing (no unsafe retry); `856b263` adds opt-in `--native-job-close-error-test` using a genuine invalid-handle Win32 close failure and verifies a second Dispose is inert; `014d062` continues direct-child wait/termination even if Job close fails, retaining multiple cleanup errors.
- `ClamJob.Attach` retains both original setup/assignment failure and cleanup failure through `AggregateException`. **Code committed; Windows acceptance pending.** No local .NET SDK available in the assistant execution environment; do not report a build PASS.
- Run next on Windows: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-job-close-error-test` then `powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\regression-test.ps1`.
- The invalid-handle test exercises an actual failing `CloseHandle` but has **no live Job member**; ordinary existing Job-close regression tests real termination on successful close. Failure of Job close leaves descendant-termination status **unconfirmed**. Further native fault/descendant work remains before production integration.
- Draft PR #3 stays draft/unmerged; `ClamProcessRunner` and `main` unchanged.

## CURRENT HANDOFF — 2026-10-20 (LATEST; start here)

**Branch:** `feature/m3-clamav-adapter` in public GitHub repository `richardlou12717/MiniAV`; [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) must remain Draft/unmerged. `main` retains M0–M2. Do not switch the production `ClamProcessRunner` without explicit review and Windows acceptance. The shipping runner still uses `Process.Start` before `ClamJob.Attach` (startup race). Experimental `WindowsSuspendedLauncher` attaches Job before resume but is **not integrated**.

**Milestone:** M3.2 diagnostics validated; M3.3 production-integration safety hardening active. **Most recent user Windows acceptance:** `--native-timeout-cleanup-double-fault-test` PASS on 2026-10-20. User's brief "通过了" did not distinguish whether the separately requested `--native-overlapped-failure-test` was rerun; prior PASS remains valid, no new PASS asserted.

**Recent GitHub commits (chronological):**
- `3f204a1` deferred-disposal error observability; `1caa209` 80-iteration overlapped stress suite.
- `369dbaf` recorded Windows stress PASS; 80 rounds handle-count deltas at rounds 20/40/60/80 were **−1, +4, +9, +8**, respectively (final +8; no proof of leak freedom).
- `414c0e8` M3.3 safety review document; `071772d` native timeout+cleanup double-fault fix and new opt-in regression.
- `888ddc9` recorded Windows PASS for timeout+cleanup double fault. Earlier deferred cleanup observability and exactly-once resource ownership also Windows PASS.

**Known-good opt-in Windows diagnostics:** `--native-reader-deferred-cleanup-test` (two PASS lines: disposal fault visibility and exactly-once ownership); `--native-overlapped-stress-test` (80 runs PASS, +8 final handles); `--native-timeout-cleanup-double-fault-test` (PASS). Older tests documented below include suspended stdio, pre-resume faults, cancellation, timeout, flood, Job descendants, retained writer, forced reader join, cleanup faults, and repeated failure paths.

**Prioritized next tasks (no pending code change required for handoff):**
1. **P0:** fix `ClamJob.Dispose()` ignoring return value of Win32 `CloseHandle`; design testable failure reporting without double-close or assuming a post-close synthetic fault equals an actual failing native close. Preserve exception context during `ClamJob.Attach` failures; avoid modifying production runner behavior until tested.
2. **P0:** address possibility of actual permanently blocked reader/kernel I/O despite `DeferredReaderCleanup` retained ownership. Verify overlapped cancellation and terminal resource release with real retained writer; no assumption that bounded join alone proves quiescence.
3. **P1:** specify adapter mapping for native `InvalidDataException` output overflow to production `ProcessOutputLimitException`, and ensure the future integration explicitly opts into overlapped output pipes. Review argv, 0/1/2 exit codes, stderr, memory Job and abort paths.
4. Check fresh Windows CI for **current branch HEAD**, then run core/ClamAV complete regression, real harmless ClamAV smoke and WPF cancel/restart/close before proposing integration. Previous Actions status `in_progress` was **not CI PASS**.

**Source:** `docs/M3_3_PRODUCTION_INTEGRATION_REVIEW.md` and earlier sections below. Review notes were written before the timeout fix; the timeout preservation issue has now been resolved and locally accepted. Other blockers remain.

**Start commands (Windows PowerShell):**
```powershell
cd C:\Projects\MiniAV
git switch feature/m3-clamav-adapter
git pull --ff-only
$project = ".\tests\MiniAV.ClamAV.Regression\MiniAV.ClamAV.Regression.csproj"
dotnet run --project $project -c Release -- --native-timeout-cleanup-double-fault-test
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1
```

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

### M3.2 post-integration overlapped failure tests — Windows PASS (2026-10-19)

- User reran `--native-overlapped-failure-test` **after** integrating `DeferredReaderCleanup` into the isolated launcher and reported PASS. Active-output cancellation, execution timeout and output overflow each printed `both readers terminal before stream disposal`, followed by `M3.1 overlapped cancellation timeout overflow reader quiescence passed`.
- Combined with the post-integration PASS of the retained-writer fixture, cleanup double-fault test and full regression script, ordinary observed native failure paths currently regress cleanly.
- **Still open**: force an actual native launcher reader-join timeout, verify the deferred continuation owns all Stream/pipe/CTS resources until final reader completion; show safety for failures during Job close and deferred disposal and evaluate irrecoverably stuck reads, sustained handle trend, Windows CI. Do not claim universal resource safety or production readiness.
- `ClamProcessRunner` unchanged, PR #3 Draft and `main` untouched.

### M3.2 forced native launcher reader-join timeout fixture (2026-10-19; Windows pending)

- Added opt-in `--native-forced-reader-join-test` using a real suspended Windows PowerShell subprocess, Job-before-resume, and overlapped stdout/stderr. A **test-only post-read completion gate** holds the stdout reader task even after underlying read has completed/cancelled; a short test-only 150ms reader join forces the *actual launcher cleanup path* into `DeferredReaderCleanup` timeout and resource transfer.
- Fixture checks the launcher reports the expected `IOException` (not success), the deferred owner reports ownership **without early disposal**, then releases the held completion and checks eventual disposal. No production path or default 5s join limit changes.
- **Evidence boundary:** this simulates a task that has not completed **after** underlying I/O finishes; it is NOT proof of a genuine blocked kernel pipe operation being cancellable, nor proof against leaks when a reader permanently hangs.
- Windows build and execution **not yet validated**. Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-forced-reader-join-test`. If PASS, rerun ordinary overlapped-failure and retained-writer suites. PR #3 Draft; main / production runner untouched.

### M3.2 forced native reader-join timeout Windows PASS (2026-10-19)

- User ran `--native-forced-reader-join-test` after commit `14d2ccb` and reported **PASS**. Actual suspended-launch / Job / overlapped-pipe flow entered a test-injected reader-task completion delay and forced the 150ms join timeout. The test asserted fail-closed `IOException`, no premature disposal while held, then eventual disposal after releasing the completion gate.
- This validates the actual launcher's deferred-owner path in a deterministic delayed-*task*-completion test. It **does not** prove cancellation of a permanently blocked kernel I/O operation, guarantee eventual disposal for irrecoverably stuck reads, or cover all cleanup exceptions / long-run handle trends.
- Next gates: failure during Job close before ownership registration, deferred disposal failure observability, blocked real I/O and sustained resource measurements, CI. Do not switch production `ClamProcessRunner` yet; PR #3 Draft, main unchanged.

### M3.2 Job-close fault ownership gate (2026-10-19; Windows pending)

- In isolated `WindowsSuspendedLauncher`, create the `DeferredReaderCleanup` owner **before** closing the Job in reader cleanup. An exception from Job close is captured, reader joining/deferred ownership still executes, and simultaneous Job-close plus reader-join errors are preserved in an `AggregateException`.
- Added test-only `afterJobCloseTestHook` (runs after real Job close) and `--native-job-close-ownership-test`. Test holds stdout task completion, forces join timeout, injects post-Job-close exception, asserts both failures and resources remain owned until the held task is released, then eventual disposal. **No Windows PASS yet.** This does not simulate failure of the Win32 CloseHandle API itself, and further faults (e.g. exception before joining readers) remain open.
- Run: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-job-close-ownership-test`. If PASS, rerun `--native-forced-reader-join-test` and `--native-overlapped-failure-test`.
- Production scanner unchanged; PR #3 Draft; main untouched.

### M3.2 post-Job-close fault ownership Windows acceptance (2026-10-19)

- User reported **PASS** after running the new `--native-job-close-ownership-test` from commit `07d84aa`. The fixture exercises real Job closure followed by injected cleanup failure, forced reader join timeout, preservation of both failures, no premature resource disposal, and eventual disposal after the held reader task finishes.
- The user reply does not separately show output for the suggested `--native-forced-reader-join-test` and `--native-overlapped-failure-test` reruns after this latest change; their previous PASS results remain documented, but don't claim fresh reruns.
- Remaining gates: lower-level native handle failure behavior, deferred disposal error observability, genuinely stalled kernel I/O, sustained resource/handle tests, and CI. Production `ClamProcessRunner` unchanged; PR #3 Draft, main untouched.

### M3.2 deferred disposal error observability (2026-10-19; Windows pending)

- `DeferredReaderCleanup` now exposes `CleanupCompletion: Task<Exception?>`. Once reader tasks terminate and ownership cleanup runs, it returns `null` for success or an `AggregateException` collecting every disposal failure; a failed disposal no longer prevents attempting subsequent resource releases. Errors also emit `Trace.TraceError`, rather than Debug-only output.
- Extended existing `--native-reader-deferred-cleanup-test` with a fault-injected disposable and a healthy disposable: it must verify neither is disposed before the held reader finishes; afterward it inspects the exact failure and verifies both were attempted once.
- **Windows compilation and PASS pending.** Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-reader-deferred-cleanup-test`.
- Limitation: a deferred disposal exception occurs *after* the launcher has returned failure and cannot retroactively alter that thrown exception. The result is inspectable by a test/observer retaining the owner, and traced; a structured production diagnostic sink and real-process cleanup fault testing are still needed. Production runner unchanged, PR #3 Draft, main unchanged.

### M3.2 overlapped launcher stress diagnostic (2026-10-20; Windows pending)

- Added `--native-overlapped-stress-test`, opt-in 80-iteration Windows diagnostic using the isolated native suspended-launch path with overlapped output pipes. Rotates clean exit, cancellation, execution timeout and output overflow. Measures process handle counts after GC every 20 runs (four snapshots); after warmup flags a final handle increase above 40 as a potential resource leak, not a proof that smaller growth is harmless.
- **Windows build/run NOT YET VERIFIED**. Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-reader-deferred-cleanup-test` first (previously outstanding), then `--native-overlapped-stress-test`. Report the four handle snapshots exactly.
- Remain aware of known gaps: genuinely stuck kernel I/O, cleanup callback exceptions, delayed resource disposal diagnostics, CI; do not merge Draft PR #3 or replace production `ClamProcessRunner`.

### M3.2 deferred cleanup observability Windows PASS (2026-10-20)

- User ran `--native-reader-deferred-cleanup-test` after commit `1caa209` and reported **PASS**, with both `M3.2 deferred disposal error observability passed` and `M3.2 deferred reader ownership and exactly-once disposal passed`.
- This confirms the in-process held-task fixture exposes the injected disposal error and still attempts other resource disposals, as well as deferred ownership and exactly-once release. It does not establish all actual OS cleanup failure outcomes.
- The newly added `--native-overlapped-stress-test` **has not been run yet**. Next Windows command: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-overlapped-stress-test`. Record all four handle counts/deltas; inspect trajectory, not just pass/fail.
- No production integration; PR #3 Draft, main untouched.

### M3.2 overlapped 80-iteration Windows stress PASS (2026-10-20)

- User ran `--native-overlapped-stress-test` and obtained **PASS** over 80 alternating cases (successful exit, cancellation, timeout and output overflow). After warmup / GC the handle-count deltas were **round 20: -1 (266 handles), round 40: +4 (271), round 60: +9 (276), round 80: +8 (275)**. Final delta **+8**.
- The count decreased from round 60 to round 80, with no monotonic growth across all samples. This is favorable but **not proof of leak freedom**; trends over a larger run, delayed disposal, private bytes and any surviving child processes still warrant attention.
- This PASS follows Windows confirmation of `--native-reader-deferred-cleanup-test` (both error observability and exactly-once disposal PASS). Still open: genuinely nonquiescing native I/O, deeper native cleanup/Job failure scenarios, Windows CI, production integration and real-engine acceptance.
- Production `ClamProcessRunner` unchanged. PR #3 remains Draft; `main` untouched.

### M3.3 production integration security review (2026-10-20)

- Added `docs/M3_3_PRODUCTION_INTEGRATION_REVIEW.md` documenting source-level findings from `WindowsSuspendedLauncher`, `ClamJob`, `ClamProcessRunner`, and `ClamAvScanEngine`. No production code changed.
- Release blockers: `ClamJob.Dispose` currently ignores Win32 `CloseHandle` result; permanent underlying I/O stalls remain possible despite deferred ownership; original timeout and cleanup error aggregation needs a focused regression; native `InvalidDataException` output overflow needs explicit mapping to production `ProcessOutputLimitException`; future integration must select overlapped pipes, not default synchronous path.
- Related Windows CI query for commit `369dbaf` returned workflow `in_progress`, **not a green result**; it is not a current-head CI endorsement.
- Next step: targeted timeout+cleanup fault regression, then native Job close reliability design. Keep PR #3 Draft; `main` and production runner unchanged.

### M3.3 deadline timeout plus cleanup fault regression (2026-10-20; Windows pending)

- Fixed isolated `WindowsSuspendedLauncher` deadline exception preservation: its timeout-specific catch now captures a `TimeoutException` into `originalFailure` rather than throwing from that catch before the outer native cleanup-finally executes. This allows `AggregateException` to retain both timeout and a subsequent cleanup failure.
- Added Windows opt-in `--native-timeout-cleanup-double-fault-test`: harmless sleeping PowerShell, 900ms deadline and fault injected after confirmed native cleanup; asserts the aggregate contains original `TimeoutException` then `IOException` wrapping the injected cleanup failure. **NOT WINDOWS-VALIDATED YET**.
- Run: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-timeout-cleanup-double-fault-test`. If PASS, rerun `--native-overlapped-failure-test` to guard cancellation and timeout behavior. Keep other M3.3 release blockers open, especially `ClamJob.Dispose` ignoring native `CloseHandle` failures; do not modify production runner yet.
- PR #3 remains Draft; main unchanged.

### M3.3 timeout + native cleanup double-fault Windows acceptance (2026-10-20)

- User reported **PASS** after being asked to run `--native-timeout-cleanup-double-fault-test` (commit `071772d`). This checks preservation of both the execution deadline `TimeoutException` and injected post-cleanup `IOException` in an aggregate.
- The reply did not show separate command output from the requested `--native-overlapped-failure-test` rerun; do not claim a new post-fix PASS for that suite. Its earlier PASS remains documented.
- Next priority: confirm `ClamJob.Dispose`/native `CloseHandle` failures cannot be silently ignored, while avoiding unintended production path changes. Retain remaining blocked-I/O and CI gates.
- PR #3 Draft, `main` and production `ClamProcessRunner` unchanged.

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
> 继续 MiniAV 项目。请先阅读本文件顶部的 2026-10-20 最新交接，以及 docs/M3_3_PRODUCTION_INTEGRATION_REVIEW.md。当前 M3.3，优先修复 ClamJob.Dispose 忽略 CloseHandle 失败的问题。仓库现已公开，但 PR #3 仍为 Draft；不改 main、不直接替换生产 Runner。\n\n> （旧交接，仅供历史参考）继续 MiniAV 项目。请从 GitHub 私有仓库 `richardlou12717/MiniAV` 的 **`feature/m3-clamav-adapter` 分支**读取最新 `PROJECT_STATUS.md`、`docs/M3_NATIVE_LAUNCHER_PLAN.md` 和相关代码。M3 原生 `STARTUPINFOEX` stdout/stderr 原型在 Windows 本机已通过 `--native-io-test`；现在继续 M3.1 生产级原生启动器的取消、超时与资源清理设计和实现。不要合并 Draft PR #3；保持 `main` 稳定。
