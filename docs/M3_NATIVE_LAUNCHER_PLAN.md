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
