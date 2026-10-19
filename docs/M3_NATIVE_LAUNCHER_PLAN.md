# M3 — Production Suspended Launcher Integration Plan

Status: design and regression groundwork only; **not implemented in production**. Branch: `feature/m3-clamav-adapter`; PR #3 stays Draft.

## Goal
Remove the interval in which `clamscan.exe` runs between `Process.Start()` and Job attachment, without weakening cancellation, timeout, bounded output, or read-only behavior.

## Proposed Windows process boundary
1. Validate absolute executable and fixed argument list; use the same established ClamAV CLI switches and single-file target. Do not invoke a shell.
2. Create three anonymous pipes for stdin, stdout, stderr. Mark only the intended child endpoints inheritable. Use `STARTUPINFOEX` and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` with `EXTENDED_STARTUPINFO_PRESENT`, `STARTF_USESTDHANDLES`; never use unrestricted inherited handles. Explicitly close unused endpoints on both sides.
3. Construct the Windows command line using tested .NET-compatible quoting for individual arguments, not manual untrusted string concatenation. Keep application name separately supplied to `CreateProcessW`.
4. Create the process with `CREATE_SUSPENDED | CREATE_NO_WINDOW`, using the specified working directory. Capture owning process and primary-thread SafeHandles immediately; do not resume yet.
5. Apply per-process and aggregate Windows Job Object memory limits and kill-on-close; attach the suspended process. If configuration/assignment fails, terminate and wait for the child before disposing handles. Never fall back to unbounded launch.
6. After successful attachment and a cancellation check, resume the main thread. Close the parent-side stdin writer (ClamAV must not wait for input), drain stdout and stderr concurrently into bounded buffers, and watch the child and deadline.
7. On cancel, timeout, reader overflow, exceptions, or disposal, close the Job to kill members, wait for child termination with bounded wait and explicit failure reporting, and close every native handle. Do not convert failures into `NoMatch`.

## Mandatory tests before switching production
- Native launcher fixture: stdout, stderr, exit 0/1/2, arguments containing spaces and Unicode, exit parser compatibility.
- Pre-resume failure and cancellation: no child execution, no suspended orphan; verify Job membership before resume.
- Timeout and user cancel: terminate descendants and drain both pipes; no deadlock under stdout/stderr flooding.
- Output cap: deterministic failure; many repetitions under Windows CI to detect handle leaks.
- Nested Job / denied assignment: report error, never launch unbounded.
- Real installed ClamAV 1.5.4 on an ordinary harmless file; WPF cancel/restart/close and CLI unchanged.
- Keep previous `Process.Start` runner until CI and local acceptance pass. Do not remove it merely because the standalone `cmd.exe` suspended-launch prototype passes.

## Security qualifications
Job Object is not an OS sandbox. Suspended process creation plus Job assignment reduces the launch-before-attachment race but does not establish executable authenticity, signature database authenticity/freshness, or complete scan coverage. CPU hard capping and robust input snapshot semantics remain separate decisions.

## Implementation checkpoint — child pipe handles (2026-10-18)
- Added `WindowsChildPipe` as a standalone disposable Windows anonymous pipe owner; only the designated child endpoint is inheritable. This is not yet passed to `CreateProcessW` and it is not a substitute for the planned explicit `STARTUPINFOEX` handle allowlist.
- Added `NativePipeRegression` to the ordinary Windows test suite to verify parent/child inheritability flags in both stream directions. Windows CI and local behavior must pass before further integration.
- Current production `ClamProcessRunner` remains unchanged. Next stage: explicit inherited handle list, safe stream ownership, stdout/stderr draining, and Job-before-resume lifecycle.

## Native redirected-I/O prototype (pending Windows verification)
- New opt-in regression: `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-io-test`.
- Uses `STARTUPINFOEX`, a three-handle `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, `CREATE_SUSPENDED`, Job attachment before `ResumeThread`, and redirected stdout/stderr from harmless `cmd.exe`.
- Production scanner **unchanged**; native test may still need Windows-specific adjustments. Validate GitHub CI compilation, run opt-in fixture locally, and investigate any hangs/handle leaks before production integration. The standalone fixture has a 10-second child deadline and failure cleanup.


## M3.1 isolated launcher checkpoint (2026-10-18; pending Windows validation)
- Introduced `WindowsSuspendedLauncher.RunAsync` (not wired into `ClamProcessRunner`). Builds an argv-safe command line, creates a child suspended with `STARTUPINFOEX` three-stdio-handle allowlist, attaches `ClamJob` before resume, and drains stdout/stderr concurrently with per-stream character limits.
- Checks cancellation before launch and resume; a linked deadline drives timeout. A fail-fast completion loop reacts to overflow, cancellation, and process/read failures without awaiting unrelated blocked pipe readers before entering cleanup.
- Cleanup closes Job first (kill-on-close), then terminates and waits briefly for a still-active main child, disposes owned pipe/attribute/process/thread resources. A 5-second failed wait is currently only logged with `Debug.WriteLine`; **strong cleanup confirmation and explicit failure reporting remain incomplete**. Pipe readers may still be unwinding after cancellation. Not production-ready.
- Added separately opt-in `--native-launcher-test` for stdout/stderr/exit, pre-launch cancel, running cancel, timeout, and simultaneous output flood. **Not yet run on user Windows or confirmed by CI**. Run `dotnet run --project .\\tests\\MiniAV.ClamAV.Regression\\MiniAV.ClamAV.Regression.csproj -c Release -- --native-launcher-test`.
- Still outstanding: post-create/pre-resume cancellation injection; orphan/descendant tests; repeated handle-leak checks; robust termination/pipe reader quiescence; Windows acceptance; real ClamAV compatibility and production integration. Prior Process.Start-based runner remains active.

## M3.1 latest decision gate (2026-10-19)

**Isolated implementation exists and passed local Windows smoke/failure-path tests, but is NOT wired into production.** See `PROJECT_STATUS.md` → `CURRENT HANDOFF` for exact runs, handle deltas and next steps.

Critical technical finding: `WindowsChildPipe.Create` uses `CreatePipe`, yielding synchronous anonymous pipe handles. `FileStream(isAsync:true)` was rejected by Windows/.NET; reverted to `isAsync:false` and retested. The bounded five-second Task join is insufficient evidence that a synchronous-backed read always stops before its `StreamReader`/`FileStream` is disposed. **Resolve this before M3.4** with a defensible cancelable-I/O strategy and instrumented proof, not by altering the `isAsync` flag.

The other corrected native cleanup sequence is: close Job; wait for process exit; only if timeout attempt fallback termination and reconfirm process exit; report unsuccessful termination/wait fail closed. Immediate fallback previously produced Win32 Access Denied (5); after correction, native smoke, descendant timeout/overflow and 30-run exceptional fixtures passed on user Windows.

**Remaining gates:** deterministic pipe-read task quiescence; native cleanup error/fault injection; long exceptional-path handle trend (last 30-run failure suite +8 handles); nested Job/denied-attach; Windows CI opt-in coverage; harmless real ClamAV native-launch smoke **after** integration; WPF/CLI acceptance. The production Process.Start-before-Job race remains open. PR #3 stays Draft, main untouched.
