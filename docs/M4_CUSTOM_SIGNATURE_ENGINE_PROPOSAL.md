# M4 — Custom Signature Engine (Future Proposal)

**Status:** Idea accepted for future planning; not implemented or scheduled. **Priority:** complete M3 native-launch security, production integration, and Windows acceptance before starting M4.

## Purpose

Make MiniAV a lightweight, transparent, user-customizable on-demand scanner alongside (not replacing or disabling) Microsoft Defender. An individual can register a signature and scan their own files for matching content.

## Proposed capabilities (incremental)

1. **M4.1 — User-managed SHA-256 signatures:** manually add/import a SHA-256 digest with a name, description, and optional source; list/edit/enable/disable/delete custom entries. Reuse existing hash engine and signature infrastructure where practical.
2. **M4.2 — Binary patterns:** user-defined hexadecimal byte sequences, optional fixed-offset matching and wildcard bytes. Validate syntax and impose pattern-length, count, input-size, CPU/time, and memory limits. Prefer streaming chunked scanning with correctly handled cross-chunk matches.
3. **M4.3 — YARA compatibility (optional):** evaluate a maintained, appropriately isolated and versioned YARA integration after a separate security and licensing review; do not execute user rules as arbitrary native code in the MiniAV process.
4. **Optional signature authoring helper:** select a range of bytes from a suspicious sample to generate a candidate pattern and preview matches; warn about generic/common patterns and false positives.

## Safety, usability, and data model

- Separate **signature definitions / storage** from `IScanEngine` implementations and scanning orchestration.
- Store metadata such as rule ID, type, pattern/hash, display name, created/updated date, enabled flag, origin, and version. Validate imports strictly and support safe export; avoid silently executing imported content.
- Report custom hits explicitly as **Custom Signature Match**, including rule ID and matched file. A match is *not* conclusive malware identification; default action is **report only**, never auto-delete or auto-quarantine.
- Keep scanning on-demand, avoid elevated privileges and kernel drivers, and preserve cancellation, deterministic error states, file-size/output/time bounds, and reproducible tests.
- Distinguish user-authored signatures from curated third-party malware signature databases and from engine-level errors. Do not claim full antivirus coverage.

## Suggested acceptance criteria

- SHA-256 user rule round-trip persistence; matches and nonmatches with disabled rules respected.
- Hex/wildcard and cross-buffer matching; malformed rule rejection; duplicate IDs; Unicode file names; resource/time limits and cancellation.
- Benign common-pattern false-positive demonstrations; explicit Custom Signature Match UX; no automatic destructive actions.
- Regression tests for rule import/export parsing, concurrent scans and signature version consistency.

**This is a roadmap idea, not authorization to begin M4 implementation or alter M3 scope.**
