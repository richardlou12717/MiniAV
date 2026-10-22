# M3.3 — Native launcher production-integration safety review

Date: 2026-10-20. Scope: isolated `WindowsSuspendedLauncher`, `ClamJob`, `ClamProcessRunner`, `ClamAvScanEngine`. **Review only: production runner unchanged, PR #3 Draft.**

## Verified acceptance carried forward
- User-reported Windows PASS: overlapped stdout/stderr launcher smoke, cancellation/timeout/overflow, descendant-retained outputs, double-fault propagation, forced deferred reader join, post-Job-close injected fault, deferred disposal observability, 80-iteration overlapped stress.
- Stress handle samples after warmup: 20=-1, 40=+4, 60=+9, 80=+8. These are reassuring diagnostics, not proof of leak-freedom.

## M3.3 Windows reacceptance after raw handle cleanup change — 2026-10-21
User confirmed all three commands PASS: full `regression-test.ps1`, isolated `--native-timeout-cleanup-double-fault-test`, and `--native-adapter-compatibility-test`. These are user-reported Windows results, not independently captured console logs. Handle-close error collection is now present, but real failing native close of a valid Job/process/thread handle and permanently stuck kernel I/O still lack guaranteed recovery. Preserve these as release blockers; do not integrate default Runner or merge PR #3.

## M3.3 native raw handle-close observability change — 2026-10-21 (pending Windows validation)
The isolated launcher now inspects process/thread `CloseHandle` Boolean results and captures Win32 error immediately; errors are accumulated rather than silently ignored or prematurely preventing the remaining native close operations. `child.Dispose` exceptions are likewise recorded. This retains the original/cleanup aggregate exception behavior. No default production Runner change; run full Windows regression plus timeout double-fault and native adapter compatibility opt-in tests. Successful handle-close regressions do not certify a genuinely failing Job close of its only live handle or a forever-stalled kernel read.

## M3.3 CI verification and disposition — 2026-10-21
- GitHub Actions query for `44905ef8c358db6e2e1ca4dde2a688320476bc56` returned two Windows CI workflows **completed/success**. PR #3 remains draft, open, unmerged.
- Existing user Windows PASS for full regression and isolated real harmless ClamAV smoke remains accepted, but no post-integration WPF or CLI acceptance has been run because the default Runner was deliberately not changed.
- Remaining P0 boundaries: actual first-close failure of a live valid Job handle is not shown to kill descendants; `DeferredReaderCleanup` cannot force completion of a genuinely indefinitely stuck underlying read. Native launcher also ignores Boolean return values from `CloseHandle` on raw process/thread handles in its outer finally; these may conceal cleanup faults. Design explicit error collection and original-error preservation before production migration.
- **Decision**: remain at M3.3, no production Runner replacement, no PR ready/merge and no main changes until these boundaries are addressed and Windows reacceptance is obtained.

## M3.3 real ClamAV smoke and full regression Windows PASS — 2026-10-20
User replied PASS to the pair of requested checks: full `scripts/regression-test.ps1` and `--native-real-clam-smoke-test` with the installed `clamscan.exe`. The real test exercises a harmless text file via explicitly injected native prototype and requires NoMatch. These are user-reported results without attached logs. Neither test certifies signature database freshness, complete detection coverage or readiness to replace the production Runner. Remaining release blockers include current-head CI, real failed first Job close semantics and pathological unresolved I/O.

## M3.3 isolated real ClamAV smoke fixture (Windows pending)
- Added explicit `--native-real-clam-smoke-test` with an existing absolute `clamscan.exe` path and optional database directory. It injects the internal `NativeClamProcessRunnerPrototype` into `ClamAvScanEngine`, creates a harmless temporary text fixture and requires `ScanStatus.NoMatch`. Thus it covers real argv generation, native suspended/Job-before-resume overlapped child launch, output parsing and exit behavior without changing the production Runner.
- A PASS is evidence only for the scanned harmless file with the installed database, not signature freshness or comprehensive coverage. Unexpected engine output, stderr, errors and cleanup failure must fail rather than be reported clean.
- New full regression and real native smoke Windows results are pending. No production migration, main merge or PR state change.

## M3.3 native compatibility Windows PASS — 2026-10-20
User reports PASS for the opt-in `--native-adapter-compatibility-test` on Windows. The test covers harmless native exit codes 0/1/2 and stdout/stderr, output overflow exception mapping, execution deadline and caller cancellation with explicitly selected overlapped output pipes. Full regression after this change and real clamscan.exe integration are not yet confirmed. Production `ClamProcessRunner`, main and Draft PR #3 remain unchanged.

## M3.3 isolated native adapter compatibility (2026-10-20; Windows pending)
- Added an internal, non-production `NativeClamProcessRunnerPrototype` implementing `IClamProcessRunner`. Explicitly opts in to overlapped output pipes, returns the production `ProcessExecution` shape, and translates only the known native output-cap `InvalidDataException` to `ProcessOutputLimitException`; unrelated native errors and double faults are not rewritten.
- Added `--native-adapter-compatibility-test` to validate exit 0/1/2 with stdout/stderr, overflow translation, timeout and caller cancellation against a harmless native PowerShell child. **Windows result pending.** This does not replace default `ClamProcessRunner`, authorize production integration, validate real ClamAV output, or close outstanding I/O/kernel failure risks.
- Production limits mirrored: max 32,768 output characters per stream, 2 GiB memory. Explicit API/launch compatibility and end-to-end smoke remain later gates.

## M3.3 retained-descendant overflow Windows PASS — 2026-10-20
User reported PASS for `--native-retained-writer-overflow-test`. With prior cancellation and timeout acceptance, the three targeted failure modes now have Windows-tested isolated launcher/Job/overlapped-reader cleanup scenarios involving a descendant. The overflow fixture does not require direct parent exit before overflow, and none of these tests establishes all possible kernel-I/O failure or native Job close scenarios. Remaining blockers are explicit adapter error translation, forcing overlapped pipe use on eventual production integration, native close failure boundaries, and fresh end-to-end/CI verification.

## M3.3 retained-descendant timeout Windows PASS — 2026-10-20
User ran `--native-retained-writer-timeout-test` and reported `M3.3 retained-writer descendant timeout and paired reader cleanup passed`. This validates one real suspended native launcher deadline scenario where parent exits while descendant retains output writers and both readers remain pending; Job cleanup terminates the marked descendant and readers finish. The complementary `--native-retained-writer-overflow-test` is committed but has not yet been run on Windows.

## M3.3 retained-descendant overflow fixture (Windows pending)
Added `--native-retained-writer-overflow-test`: real suspended native launcher, Job descendant marker before both-stream flood, expected native `InvalidDataException` overflow, both reader tasks terminal and no living marked descendant after launcher returns. A separate timeout fixture also remains pending Windows validation. This overflow fixture does not assert direct parent exit with pending readers, and does not prove handling of genuinely failed first close of a valid Job. No production integration.

## M3.3 retained-descendant deadline fixture (pending Windows)
Added isolated `--native-retained-writer-timeout-test`. Parent exits while the two real overlapped output reads remain pending due to a spawned descendant; the native timeout must close Job, stop descendant, and observe both readers terminal. This is the distinct deadline version of the previously accepted cancellation fixture. The test is unverified on Windows; it does not show cleanup guarantees after actual first-close failure of a valid Job handle. Production Runner and main unchanged.

## M3.3 paired real blocked-reader Windows PASS — 2026-10-20
User reported PASS for `--native-blocked-read-paired-test`. Shared deferred ownership remained until both actual overlapped pipe reads were terminal. This establishes the bounded test conditions, not universal kernel I/O quiescence or suspended-child descendant guarantees.

## M3.3 paired stdout/stderr pending-read ownership (pending Windows)
New opt-in `--native-blocked-read-paired-test` creates two real overlapped output pipes and deferred-joins their pending reads under one owner. Closing stdout's writer must not dispose resources while stderr remains pending; closing stderr's writer permits terminal completion and disposal. This test is pending Windows acceptance and does not establish an unconditional kernel-I/O cancellation guarantee.

## M3.3 deferred-owner Windows acceptance (2026-10-20)
User reported PASS for both `--native-blocked-read-deferred-test` and `--native-blocked-read-deferred-cancel-test`. Tested actual overlapped read pending while owner defers cleanup, then EOF after writer close or cancellation while writer stays connected, followed by cleanup completion. This narrows (but does not eliminate) the kernel I/O lifetime blocker. No evidence yet for permanently stuck reads or complete native-launcher/descendant behavior.

## M3.3 deferred cancel complement — 2026-10-20
Added `--native-blocked-read-deferred-cancel-test`: pending actual overlapped read, deferred owner after join timeout, cancellation with writer still connected, terminal read verification and observable ownership disposal. This complements the deferred EOF case. Both new deferred-owner fixtures are awaiting user Windows results. Neither proves resolution of permanently non-terminating kernel I/O. Native launcher production switch remains blocked.

## M3.3 real I/O Windows acceptance and follow-up (2026-10-20)
- User reported PASS for `--native-blocked-read-test`, verifying cancellation of actual pending named-pipe reads and EOF after writer close.
- New pending Windows fixture `--native-blocked-read-deferred-test` connects an actual pending read to `DeferredReaderCleanup`, verifies ownership is not released during a bounded join timeout, then releases the writer and asserts observable deferred cleanup completion. It is a controlled EOF test, not proof of shutdown under permanently stuck I/O.
- No production runner or main change; Draft PR #3 remains Draft.

## M3.3 real blocked-read experiment (2026-10-20; Windows pending)
An opt-in `--native-blocked-read-test` now creates a real overlapped named-pipe server and a connected idle writer, verifies the first read stays pending, cancels it and demands `OperationCanceledException`; then verifies a second pending read yields zero-length EOF after writer close. Bounded watchdogs reject a stuck read rather than call it quiescent. This is a focused I/O primitive test, not proof that the entire native launcher handles permanently stuck I/O. Production `ClamProcessRunner` remains unchanged.

## M3.3 Windows live-Job acceptance — 2026-10-20
User reports PASS for `--native-job-external-close-test`. This establishes the fixture's successful close of the last Job handle, observed child termination and correct stale-handle error reporting on subsequent wrapper Dispose. It **does not** show that descendants terminate if the *initial* valid close itself fails. That safety property is still not validated. Keep native production integration gated.

## M3.3 follow-up live Job test (pending Windows execution)
- Full `scripts/regression-test.ps1` PASS reported by user (no detailed log provided), after focused `--native-job-close-error-test` PASS.
- Added optional `--native-job-external-close-test` with a harmless live Job member. Intentionally close its Job handle **successfully** via a test-only helper, then require wrapper Dispose to report the resulting stale-handle `ERROR_INVALID_HANDLE` and assert worker exit. Verify exactly-once wrapper ownership.
- This does not model a failing CloseHandle on an otherwise-valid last live Job handle: descendant fate on that event remains unproven. Do not weaken the distinction or merge PR.
- Windows run still pending.

## M3.3 focused Windows acceptance (2026-10-20)
User reported PASS after switching the invalid-handle close fixture away from Windows' `-1` pseudo-handle to `0x12345`. This confirms the focused `--native-job-close-error-test` path (real Win32 error reporting and inert second Dispose) on the user's Windows system. Full regression PASS was not separately identified. Live Job-descendant behavior when the native close fails remains unverified and release-blocking.

## M3.3 Windows fixture correction (2026-10-20)
The first Windows run of `--native-job-close-error-test` failed because the fixture used `-1`, which is the current-process pseudo-handle on Windows, not a reliable failing `CloseHandle` input. Changed to a deliberately unaligned non-pseudo value `0x12345` (commit `4e497d8`). User rerun pending; do not claim CloseHandle error-path PASS.

## M3.3 Job close work (2026-10-20, awaiting Windows acceptance)
- `ClamJob.Dispose` now checks the Boolean result of native `CloseHandle` and throws a `Win32Exception` with the captured error; ownership is atomically consumed before attempting close, so failed-close state is not retried or mistaken for success. The design cannot reliably recover an ambiguously closed native handle.
- `ClamJob.Attach` now preserves both original attach/configuration error and any close error in an `AggregateException`.
- Isolated `WindowsSuspendedLauncher` outer cleanup now continues to wait for or terminate the **direct child** after a Job close exception and preserves multiple cleanup faults. This does not establish descendant termination on failed Job close.
- Added opt-in `--native-job-close-error-test`: `ClamJob.FromInvalidHandleForTest` uses a nonzero invalid handle to trigger genuine Win32 `CloseHandle` failure (expected `ERROR_INVALID_HANDLE=6`); second Dispose is inert. No child is attached during this negative-path test. Existing ordinary Job test covers successful Job-close child termination. **No Windows PASS yet.**
- Changes committed on feature branch; production runner and main untouched. Build/CI and native test results pending. Run focused test and full regression before any further integration.

## Release-blocking findings

### P0. Native Job close failure observability — implementation committed; Windows test pending
Previously, `ClamJob.Dispose` exchanged its handle to zero and invoked Win32 `CloseHandle` without inspecting the Boolean result. This makes the assertion “Job close succeeded/terminated descendants” incomplete for native-error scenarios. Design a close method that captures `GetLastWin32Error`, propagates failure without attempting a dangerous second close of an ambiguous handle, and demonstrates child termination/cleanup guarantees under faults. Avoid treating a synthetic post-close exception as a real failing Win32 close.

### P0. Reader quiescence if underlying kernel I/O never completes
`DeferredReaderCleanup` preserves dependent streams until reader tasks reach terminal state, and returns failure on a bounded join. It does not force a permanently stuck kernel read to complete: resources can remain retained indefinitely. Clarify guarantees for overlapped cancellation and teardown, and inject a genuine retained-writer/blocked-read failure separate from a test-only completion gate.

### P1. Timeout fault aggregation — RESOLVED; Windows PASS 2026-10-20
`WindowsSuspendedLauncher` converts timeout cancellation to `TimeoutException` in a dedicated catch preceding `catch(Exception)`. The dedicated timeout catch now captures the timeout into `originalFailure` rather than throwing immediately. Added `--native-timeout-cleanup-double-fault-test` that asserts timeout and cleanup failure remain present; user reported Windows PASS on 2026-10-20. The test injects a post-confirmed-cleanup fault, not a real failed native CloseHandle.

### P1. Output-limit exception compatibility
`WindowsSuspendedLauncher.ReadBoundedAsync` uses `InvalidDataException` for overflow, while production `ClamProcessRunner` uses `ProcessOutputLimitException` and `ClamAvScanEngine` translates it to ScanStatus.Error. Introduce an explicit error taxonomy/translation at the future adapter boundary; do not change production handling before verified compatibility tests.

### P1. Force overlapped I/O in eventual production path
The isolated native launcher defaults to synchronous anonymous `CreatePipe` mode unless `useOverlappedOutputPipes=true`. Production integration must not accidentally select this older path. Review pipe creation, explicit inherited handles, parent stream ownership, named-pipe access control and connect behavior before switch.

### P1. Existing production launch-start race remains
`ClamProcessRunner` calls `Process.Start` before `ClamJob.Attach`. It is deliberately preserved until native integration is proven, but is the principal security reason for this milestone. Avoid claiming the shipping scanner has Job-before-resume semantics.

### P2. Diagnostics, CI, end-to-end acceptance
Deferred disposal results are inspectable via `CleanupCompletion` and traced, but no durable production diagnostic contract is implemented. Review run status for the current head rather than inferring from earlier runs; current lookup for commit `369dbaf` returned Windows CI `in_progress`, not a PASS. Require fresh Windows CI, full Core and ClamAV regression suites, real harmless ClamAV smoke with correct argv/exit/status behavior, and WPF cancellation/restart/close before considering PR ready.

## Ordered next work
1. **Complete:** targeted timeout + cleanup-double-fault regression, original timeout preservation, user-reported Windows PASS.
2. Design/test observable `ClamJob` handle-close behavior, including live descendant verification, without swallowing native errors.
3. Define native-launch adapter error translations; test exit codes 0/1/2, stdout/stderr, overflow and timeout semantics.
4. Validate genuine blocked overlapped I/O teardown; investigate all unresolved owner lifetimes.
5. Confirm latest GitHub Actions result, rerun Windows suite and real ClamAV harmless sample smoke.
6. Only then propose isolated production integration on feature branch; no merge without user's permission.

**Explicitly excluded:** production `ClamProcessRunner` replacement, PR #3 ready-for-review transition, or merge to `main` at this stage.
