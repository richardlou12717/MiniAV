# MiniAV — Project Status / Cross-session Handoff

Last updated: 2026-10-17
Status: M0 complete; M1 read-only scan prototype validated on Windows; modularization pending.

## Source of truth
- Private GitHub repository: https://github.com/richardlou12717/MiniAV
- Main branch: `main`
- Windows working directory: `C:\Projects\MiniAV`
- Architecture and requirements: `docs/00_PROJECT_CHARTER.md` through `docs/07_CHANGELOG.md`
- Use the repository's latest commit and files over any older chat summary.

## Scope and technical choices
- Windows x64 personal, local, on-demand antivirus *scanner*; not a Defender replacement.
- C# / .NET 10 (SDK 10.0.402 successfully run on user's Windows machine).
- Hybrid roadmap: own scanning framework and SHA-256 demo engine; optional separate ClamAV adapter later.
- CLI first, WPF GUI later. No kernel driver or real-time system interception in MVP.
- Default read-only; never auto-delete, alter or execute scanned files. `NO_MATCH` is not a declaration of safety.

## Implemented in GitHub
- `src/MiniAV.Cli/`: initial SHA-256 file/directory scanner.
- `signatures/demo-hashes.json`: demo database v0.2 containing harmless fixture signatures for LF and CRLF variants.
- `tests/fixtures/`: harmless demo and ordinary text files.
- `scripts/smoke-test.ps1`: build and scan smoke test.
- `.gitattributes`: fixture LF normalization for future checkouts.
- `docs/`: charter, requirements, architecture, security model, roadmap, ADRs, engineering guidelines and changelog.

## Verified by user on Windows
- Git status: `main`, up to date with `origin/main`, clean worktree (at verification time).
- `dotnet build src\MiniAV.Cli`: succeeded.
- `dotnet run --project src\MiniAV.Cli -- --path tests\fixtures --db signatures\demo-hashes.json`:
  - `MATCHED [DEMO-0001-WIN: DemoOnly]` for `demo-sample.txt`
  - `NO_MATCH` for `ordinary-sample.txt`
  - `SUMMARY Matched=1 NoMatch=1 Skipped=0 Errors=0 Database=demo-0.2`
- The specific PowerShell smoke-test wrapper was not yet confirmed separately.
- This is a fixture-only functionality test, not proof of malware detection coverage.

## Next tasks (M1)
1. Create `MiniAV.Core` with `IScanEngine`, scan outcomes and orchestration contracts.
2. Extract SHA-256 engine and signature store from CLI into `MiniAV.Engines.Hash`.
3. Keep CLI arguments and result semantics compatible.
4. Add automated tests for positive/negative/error/reparse-point cases, including LF/CRLF fixtures.
5. Add GitHub Actions for Windows build and tests; ask user to validate locally.
6. Update this status and `docs/07_CHANGELOG.md` after each milestone.

## Collaboration
- User owns project and validates Windows builds/runs.
- ChatGPT serves as architect and senior programmer during active sessions.
- Prefer feature branches and pull requests for substantial changes; `main` is the stable baseline.
- Never commit passwords, personal files, malware samples, quarantine content or private paths.

## Next-session starter
"Continue MiniAV in richardlou12717/MiniAV. Read PROJECT_STATUS.md and current repository files, then proceed with M1 modularization."
