# MiniAV — Requirements (Initial Scope v0.1)

This document records early requirements and proposals. **It is not a claim that every row is implemented.** See [README](../README.md) and [project status](../PROJECT_STATUS.md) for delivered features.

| ID | Requirement | Original target |
|---|---|---|
| FR-01 | Scan a selected file or directory | M1 |
| FR-02 | Match streaming SHA-256 hashes against a local demonstration database | M1 |
| FR-03 | Distinguish Matched, NoMatch, Skipped, and Error | M1 |
| FR-04 | Skip symlinks, junctions, and other reparse points | M1 |
| FR-05 | Cancellable scans and structured reporting | M2 |
| FR-06 | Windows desktop GUI | M2 |
| FR-07 | Reversible quarantine with explicit user authorization and audit | Proposed; not implemented |
| FR-08 | Optional ClamAV process adapter | M3; experimental Draft PR |
| FR-09 | Authenticity checks, versions, and rollback for signature updates | Proposed; not implemented |
| FR-10 | Incremental scanning and further performance improvements | Future |

A file that does not match a demonstration signature must **not** be reported as safe. Permission failures and inaccessible paths cannot be silently counted as negative results.

Any future quarantine mechanism would require explicit consent, hash revalidation, path-safety checks, and recovery support.

Acceptance requires Windows builds, harmless sample testing, exit-code checks, and relevant failure-path regression tests.
