# MiniAV — Architecture

## Stable `main`

```text
   CLI / WPF
       |
 ScanCoordinator
       |
   IScanEngine
       |
  HashScanEngine
       |
 Local SHA-256 demonstration signatures
       |
 Results / bounded UI logs
```

The Core coordinates scan requests, results, cancellation, resource limits, and scan lifecycle. The hash engine performs streaming reads and matches demonstration signatures. CLI and WPF are separate frontends.

Key contracts and concepts include `ScanRequest`, `IScanEngine`, engine results, scan outcomes, and explicit failure/incomplete-scan reporting.

## Experimental M3 (not merged into main)

The [Draft ClamAV adapter PR](https://github.com/richardlou12717/MiniAV/pull/3) explores an optional external `clamscan.exe` process, including safe argument handling, cancellation, output collection, and Windows process-lifecycle cleanup. It is not the default stable runner and remains under safety review.

## Future concepts, not delivered features

A `QuarantineService` could be separated from scanning and invoked only following explicit authorization and integrity revalidation. Such remediation is **not part of the current implementation**.

MiniAV does not disable Microsoft Defender. External engine packaging, licensing, database updates, and safety constraints require separate review.
