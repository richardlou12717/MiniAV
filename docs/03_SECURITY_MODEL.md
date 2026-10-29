# MiniAV — Security Boundaries and Threat Model

## Protected assets

User files, system stability, file-path privacy, scan-result integrity, and any future remediation state.

## Untrusted inputs

File contents and metadata, reparse points, local demonstration signatures, and any responses from optional external scanners.

## Risks and controls

1. **False positives and destructive actions:** read-only scanning in the current implementation; no automatic delete, move, or quarantine.
2. **Traversal loops and path escapes:** skip Windows reparse points and junctions during traversal.
3. **Time-of-check/time-of-use:** a file may change after scanning; any future remediation must revalidate its identity and contents.
4. **Parser exposure:** the stable engine hashes byte streams; it does not execute scanned files or unpack arbitrary formats.
5. **Signature provenance:** production signature distribution would require authenticity checks, version tracking, and rollback; not implemented.
6. **Privacy:** no default upload of file contents or paths.
7. **Incomplete scans:** Error and Skipped are distinct from NoMatch; cancellation and limits must be visible.
8. **Engine failure:** unavailable engines must be reported explicitly, not treated as clean results.
9. **Endpoint protection coexistence:** never disable Microsoft Defender.
10. **Native process isolation:** experimental ClamAV integration remains under review for cleanup, descendant lifetime, and potentially blocked I/O; tests do not establish general sandbox or malware-analysis safety.

**Release boundary:** production malware-detection coverage, safe remediation, trusted signature updates, signing, and broader security verification are not established. MiniAV must not be presented as production antivirus software.
