# M3.3 — Native launcher production-integration safety review

Date: 2026-10-20. Scope: isolated `WindowsSuspendedLauncher`, `ClamJob`, `ClamProcessRunner`, `ClamAvScanEngine`. **Review only: production runner unchanged, PR #3 Draft.**

## Verified acceptance carried forward
- User-reported Windows PASS: overlapped stdout/stderr launcher smoke, cancellation/timeout/overflow, descendant-retained outputs, double-fault propagation, forced deferred reader join, post-Job-close injected fault, deferred disposal observability, 80-iteration overlapped stress.
- Stress handle samples after warmup: 20=-1, 40=+4, 60=+9, 80=+8. These are reassuring diagnostics, not proof of leak-freedom.

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
