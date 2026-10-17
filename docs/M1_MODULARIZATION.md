# M1 modular scanner

Feature branch: `feature/m1-modular-scanner`.

- `MiniAV.Core`: scan request, engine contract, findings, orchestration and exit code policy.
- `MiniAV.Engines.Hash`: read-only SHA-256 streaming demo signature matching.
- `MiniAV.Cli`: legacy argument parsing and output formatting.
- `scripts/regression-test.ps1`: Windows regression checks.
- `.github/workflows/windows-ci.yml`: Windows build and regression runner.

**Security:** no scanning result implies a file is safe; matched demo signatures are not malware intelligence. No file mutation or execution. Reparse points are skipped. Files can still be changed by another process while scanning, so scan results are not suitable as quarantine authorization without revalidation.

**Verification:** Code submitted for CI and local Windows validation; no successful build is claimed yet.
