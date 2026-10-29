# M3 — Optional ClamAV Out-of-Process Adapter (Design v1)

Status: design proposal, implementation and acceptance tests pending. Branch: `feature/m3-clamav-adapter`. Base: `main` (M2 merged).

## Goal and scope
Add an **opt-in, read-only** ClamAV scan engine for Windows x64/.NET 10 without modifying Defender, downloading databases, moving/deleting files, opening network listeners, or executing targets. Preserve the current Hash engine and CLI/WPF defaults. A `NoMatch` result is **not** a safety certificate.

## Existing contract
`MiniAV.Core/Scanning.cs` defines `IScanEngine.SignatureVersion`, `ScanFileAsync(string,long?,CancellationToken)`, five `ScanStatus` values, and `ScanCoordinator`. Coordinator owns tree traversal, directory reparse-point rejection, global duration, per-file preflight, and reporting. M3 must not silently reinterpret the exit codes or partial scans. The hash engine performs a streaming file-size check; an external path-based scanner cannot guarantee the same byte limit when a file grows after preflight.

## Components and flow

```text
CLI / WPF  -- explicit ClamAV selection + executable/database configuration
       |
ScanCoordinator (unchanged in first slice)
       |
IScanEngine
  +-- HashScanEngine (existing, demo signatures)
  +-- ClamAvScanEngine (new src/MiniAV.Engines.ClamAV)
          |
          +-- executable validation and per-request policy
          +-- IClamProcessRunner (test seam)
          +-- System.Diagnostics.Process: clamscan.exe (single file, no shell)
          +-- bounded stdout/stderr capture + conservative result parser
```

Initial slice: a single `clamscan.exe` invocation per scanned file, **sequentially** through existing coordinator. This incurs costly database reloads; suitable for contract validation and small scans, not large folders. A later design can consider a long-lived locally managed `clamd` with explicit lifecycle/security controls, or a scan-batch interface. Do not weaken current per-file metrics/skip semantics just for performance.

## Configuration and executable trust
- Use a **user-configured absolute path** to `clamscan.exe`; no current-directory or ambient PATH lookup, no automatic installation/download. Verify existence and extension, and reject ambiguous relative paths. Installation instructions point to the official ClamAV distribution; signing/publisher and integrity verification are release gates, not achieved merely by path checks.
- Optional **user-configured absolute signature database directory**; if absent, allow ClamAV's configured default and report database load failures as errors, never as clean. No automatic FreshClam operation in M3.
- For first slice, require selected executable and database policy before scan starts; no silent fallback from selected ClamAV to demo Hash engine.
- Surface actual engine/database version only when reliably determined. Until then use an explicitly descriptive `SignatureVersion` (e.g. `ClamAV (version unverified)`), not a fabricated version.

## Spawn and argument policy
- `ProcessStartInfo.UseShellExecute=false`, `CreateNoWindow=true`, `RedirectStandardOutput=true`, `RedirectStandardError=true`; `ArgumentList` only, never concatenated shell command strings.
- One normalized **absolute file path** per invocation, no recursive scan flag. Explicit no-remediation options; never pass `--remove`, `--move`, `--copy`, `--log`, `--leave-temps` or arbitrary user-supplied additional switches.
- Prefer stable terse output: `--no-summary`, `--infected`, `--follow-dir-symlinks=0`, `--follow-file-symlinks=0`; add `--database=<absolute directory>` only when configured. Validate these switches on supported Windows ClamAV versions before acceptance.
- Use a controlled working directory outside the target tree. Do not elevate. Prevent stdin interaction.
- ClamAV may internally unpack/decompress data and write temporary files; **read-only means target file preservation, not zero temporary disk writes**. Document temp directory protection/cleanup and avoid persistent scan logs.
- Limit input paths to ordinary existing files; reject reparse points immediately before launch (remaining TOCTOU risk documented). Reject/handle control characters such as CR/LF in paths until output parsing is robust.

## Failure-closed status mapping
ClamAV exit codes per upstream: 0=no virus, 1=virus found, 2=error. Map:
- `0` + valid uncontested completion evidence -> `NoMatch`;
- `1` + well-formed `<file>: <name> FOUND` (for the requested file) -> `Matched` with signature name; malformed/missing detection output -> `Error`;
- `2`, other exit codes, startup failure, malformed/inconsistent output, truncation, output implying scan errors or exceeded internal limits -> `Error`;
- executable missing or not runnable / required database unavailable -> `EngineUnavailable` **only when clearly attributable to engine readiness**; otherwise `Error`.
Do **not** trust only the numeric exit code for a positive clean claim: ClamAV may skip content due to its internal size, archive, recursion or time limits. Start by enforcing conservative ClamAV limits and treat any known skipped/partial condition as `Error`; if complete-coverage evidence is not available for a particular scenario, label it incomplete rather than clean. A file modification between preflight and external read remains an acknowledged race.

## Process lifecycle and resource boundaries
- Respect the Core cancellation token, including global `MaxScanDuration`. On cancellation/timeout kill the **process tree**, await reaping, and propagate `OperationCanceledException`; coordinator converts only its own deadline into incomplete scan error. User Ctrl+C remains cancellation, not clean.
- Set a configurable per-file process timeout, linked with Core cancellation; report timeout as `Error`, not `NoMatch`. Distinguish user cancellation vs local timeout.
- Drain stdout and stderr concurrently (otherwise pipe deadlock). Bound bytes/lines, do not store unbounded child output, and if cap exceeded terminate/reap child and return `Error`. Treat stdout/stderr and signature names as untrusted text; sanitize UI-facing messages and do not create logs.
- Start with concurrency=1. Memory/CPU child limits require Windows Job Object or equivalent **before** a release-grade resource-cap claim; a timeout alone is not a memory cap. Ensure descendant cleanup when cancellation occurs. Windows Defender continues running.

## Security, provenance and updates
- ClamAV is GPLv2; we use a separate executable and must review actual bundling/redistribution obligations prior to packaging. Current proposal bundles **nothing**. Consult upstream license/legal review for distribution; do not claim that process isolation automatically resolves licensing questions.
- Download ClamAV **only** from verified official channels when the user independently installs it. The official signature source/update path is FreshClam; M3 neither downloads nor updates anything.
- Future update design: authenticity/integrity verification, atomic activation, rollback, offline behavior, stale database warnings, version audit, and failure reporting. Never silently use a bad or absent database.
- File path privacy: do not upload paths/files or persist scan logs. Child stdout may include paths, held only in bounded process memory for parsing; UI's existing exported log remains explicitly user-initiated and possibly partial.

## Deterministic test plan (no malware, no ClamAV installation required)
Create `tests/MiniAV.ClamAV.Regression` with a fake process/runner for these scenarios before invoking the real engine:
1. startup not found / access denied / bad database; 2. exit 0 clean; 3. exit 1 valid detection; 4. exit 1 without well-formed detection; 5. exit 2 and unexpected exit; 6. malformed, spoofed or mismatched-path output; 7. bounded stderr/stdout overflow; 8. user cancel and per-file timeout with child-tree cleanup; 9. paths with spaces, Unicode and leading special characters; 10. symlink/reparse rejection and changing-file race; 11. child failure must never become clean; 12. Core scan duration and restart-after-cancel unchanged.
After deterministic tests, optional real ClamAV integration smoke test with harmless files and a safe vendor-approved test signature, run **only after explicit installation**. CI must not download signatures or require ClamAV. Validate both Release builds and regression scripts on Windows.

## Delivery order / gates
1. **Design review (this document)** and update ADR-005 to Accepted for opt-in subprocess architecture, while leaving installation/distribution open.
2. Add ClamAV adapter + fake-runner unit/regression tests; do not yet expose UI switches.
3. Integrate opt-in CLI, then WPF configuration with truthful engine/database labels.
4. Run Windows CI, user local checks; draft PR. Merge only after explicit approval.
5. Consider improved performance (batch/daemon) in a separate design; do not claim large-directory scalability for per-file `clamscan`.

## Source references checked 2026-10-17
- https://docs.clamav.net/manual/Usage/Scanning.html (clamscan starts a new engine and loads DB on each call; limits and option behavior)
- https://github.com/Cisco-Talos/clamav/blob/main/docs/man/clamscan.1.in (CLI flags; exit codes 0/1/2)
- https://docs.clamav.net/manual/Usage/SignatureManagement.html (FreshClam and databases)
- https://docs.clamav.net/Introduction.html (GPLv2, ClamAV scope)
- https://docs.clamav.net/manual/Development/libclamav.html (library linking licensing constraints)

No code implementation, binary tests, CI run, installation, or Windows acceptance is claimed by this design commit.
