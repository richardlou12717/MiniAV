# MiniAV — Engineering Guidelines

- Keep `main` buildable. Develop functional changes on dedicated branches and review them via pull requests.
- Scan read-only by default. Do not execute scanned files or automatically delete, move, or quarantine them.
- Enable nullable reference checks and treat compiler warnings as errors where configured.
- Preserve distinctions between Matched, NoMatch, Skipped, Error, and EngineUnavailable.
- Avoid leaking personal file paths into shared logs; never commit secrets or unsafe malware samples.
- Demonstration signatures are for testing and must never be described as real malware coverage.
- Pass external process arguments through safe argument lists rather than command-shell concatenation.
- Test traversal, access restrictions, reparse points, file-change races, Unicode, cancellation, and error cleanup.
- Record changes, environment, test evidence, remaining limitations, and next steps.
- Do not replace the production ClamAV runner or merge Draft PR #3 without explicit authorization and release-gate review.
