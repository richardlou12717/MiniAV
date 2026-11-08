# M3.3 Safety Closure — Fail-Closed Release Gates

Date: 2026-10-24. Scope: `feature/m3-clamav-adapter` only. This document is a **review and test plan**, not an assertion that release gates have passed.

## Baseline and non-goals

- Preserve stable `main`, keep PR #3 Draft/unmerged, and leave production `ClamProcessRunner` as the default.
- `WindowsSuspendedLauncher` and `NativeClamProcessRunnerPrototype` remain explicitly selected, isolated candidates.
- Head observed before this review: `750f21354bfb8f269f5fac60dab547f8ad990069`. Windows CI for that head was **in progress** on inspection; do not mark it PASS without a completed successful result.
- User reported Windows PASS for the post-failure-path-termination full regression and four opt-in tests on 2026-10-24. These are scenario-specific reports, not a proof of all native failure cases.

## Safety invariants

1. A native launch must not resume child execution until it is assigned to a configured Job, with intended inherited I/O handles and limits established.
2. On failure/cancellation/timeout/overflow, report the scan as failed; never translate incomplete execution or unverified cleanup into `NoMatch`.
3. Where the Job handle remains valid and owned, request explicit `TerminateJobObject` before close on failed operation. A successful API return does not by itself prove every descendant exited: test observable termination.
4. A consumed Job wrapper must never be closed a second time. A failed native close is ambiguous; do not retry the numeric handle, as it may have been recycled.
5. Keep original operation error and all observable cleanup errors. Do not replace the original timeout/cancellation/overflow with a later dispose failure.
6. Never dispose pipe/overlapped-state owners while an outstanding reader could still access them. A bounded join timing out is **not** reader quiescence; any deferred ownership must remain observable.
7. A returned clean scan requires verified process completion and successful output/exit interpretation. Partial output, resource uncertainty, or native cleanup fault must fail closed.

## Risk-to-test matrix

| Risk | Current evidence | What is NOT proven | Gate |
| --- | --- | --- | --- |
| Failure-path descendant termination | User-reported PASS: overlapped failure, retained-writer timeout, retained-writer overflow after explicit pre-close termination | Genuine first `CloseHandle` failure on the only valid live Job handle | P0 open |
| Job ownership | User-reported PASS: Job close-error, external-close and double-fault fixtures | A stale/invalid handle failure simulates a valid first-close failure | P0 open |
| Overlapped stdout/stderr ownership | Blocked-read, paired-reader, deferred cleanup, cancellation/EOF and stress fixtures passed in prior reports | A permanently non-completing kernel I/O can be forcefully quiesced without retained resources | P0 open |
| Adapter contract | User-reported PASS: native compatibility and harmless real ClamAV smoke | WPF/CLI integration on a switched production Runner, broad ClamAV coverage | P1 gated |
| Native cleanup diagnostics | Native process/thread/Job close errors now captured in isolated paths | Reliable end-user/structured diagnostic delivery in all failure combinations | P1 open |
| Current HEAD CI | Windows CI run for `750f213` was in progress at inspection | Successful completion on this exact head | P1 pending |

## Proposed next engineering slices (sequential)

### A. Fault-boundary audit (no production switch)

Review all exit paths in `WindowsSuspendedLauncher`: before CreateProcess, suspended-before-Job, Job-attach failure, ResumeThread failure, normal exit, cancellation, timeout, output overflow, retained descendant and reader-join failure. For each: record owner of Job/process/thread/reader/pipe and exactly which operations can throw. Ensure every non-success route can preserve the primary exception while recording secondary native errors.

### B. Fail-closed uncertain-termination diagnostic

Design a typed internal result/exception distinguishing: `TerminationRequested`, `TerminationConfirmed`, `TerminationUnconfirmed`, and `ReaderQuiescenceUnconfirmed`. Avoid claiming that a Process handle wait proves all descendants exited. Keep the failure observable even if a direct-child fallback succeeds. No implicit retry on an ambiguously closed native Job handle. Consider OS-assisted observation of Job process membership only while a valid Job handle remains owned, and clearly document when observation is impossible.

### C. Genuine fault-injection boundary, not a misleading simulation

Test injection should intercept operations *before* releasing ownership, with an explicit contract identifying whether native close was attempted, succeeded, failed, or left unknown state. Do not represent synthetic exception-after-success or stale-handle error as first-close failure. Use bounded harmless processes and cleanup even when assertions fail; prohibit a test PASS when descendant status remains unknown.

### D. Overlapped I/O lifecycle audit

Explicitly document that `CancelIoEx` requests cancellation but does not by itself mean the pending read is complete. Ensure close/disposal only follows actual I/O completion, and deferred cleanup remains owned if reads never become terminal. Add diagnostics and bounded fixture watchdogs; do not solve indefinite kernel I/O by disposing live buffers or blocking the GUI indefinitely.

### E. Production migration gate (future, approval required)

Only after P0 closure and Windows verification: require current-head Windows CI success, full regression, opt-in native compatibility, real harmless ClamAV smoke with explicit `clamscan.exe` and local DB, cancellation/timeout/overflow descendant checks, WPF start/cancel/restart/X-close, and a clean-scan/error status review. A successful smoke tests only one harmless sample and does not prove virus-detection coverage. Keep the existing default Runner until an explicit separate approval to switch; merging PR #3 requires its own explicit authorization.

## Session disposition

M3.3 remains **not approved for production native Runner replacement**. The current safe action is targeted safety review and test/diagnostic improvement on the feature branch. Do not mark the P0 issues resolved from synthetic failures, normal-path PASS results, or an in-progress CI run.
