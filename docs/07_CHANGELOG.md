# MiniAV — Changelog

## 2026-10-17 — M2 complete, PR #2 merged
- Merge commit: `64e366996d19e4d2e02bd3540326c883b57f35e5`.
- Added Windows WPF GUI with file/folder/signature DB selection, live counters, cancellation, incomplete-scan status and manual Save log.
- Added optional Core `MaxFiles`, `MaxFileBytes` and `MaxScanDuration`, including streaming byte cap and fail-closed timeout.
- Added batched UI rendering, bounded pending queue (2,000 records) and approximately 500k-character visible log retention. Truncated manual export clearly marks partial output; complete historical file log is not retained.
- Added `ScanCloseGate` shared with WPF and deterministic lifecycle regression. This is not full native-window UI automation.
- Windows local Core/CLI regressions and WPF Release build passed. Final PR head had two successful GitHub Actions checks; CI builds WPF and CLI on Windows.
- Constraints remain: read-only demo SHA-256 scan only, no production malware coverage, quarantine or update service.

## 2026-10-17 — M1 complete, PR #1 merged
- Modular Core scan coordinator and independent SHA-256 hash engine.
- Preserved CLI parameters, output semantics, and exit codes.
- Added Windows CI, PowerShell regression tests, simulated I/O/access-denied tests, junction/reparse-point skipping, LF/CRLF fixtures and cancellation support.
- Added incomplete-scan error handling, including MaxFiles.

## 2026-10-17 — M0 prototype
- Selected C#/.NET 10 architecture: local on-demand scanner with optional external engine later.
- Established security model, project charter, engineering rules, demo signatures and harmless fixtures.
- Built initial read-only CLI proof of concept.

Next: plan M3 on a new branch from `main`, beginning with ClamAV process adapter/security/licensing design.
