# MiniAV — Windows On-Demand File Scanner

MiniAV is an **open-source, experimental Windows file-scanning project** built with C# and .NET 10. Its purpose is to explore modular scanning, safe failure handling, resource limits, and repeatable testing—not to provide production antivirus protection.

**Stable `main`:** M0/M1/M2 completed (CLI, WPF desktop UI, modular scan coordinator, and local SHA-256 demonstration signatures).

**Under development:** M3, an optional out-of-process ClamAV adapter, remains in [Draft PR #3](https://github.com/richardlou12717/MiniAV/pull/3) and **has not been merged into `main`**. Its experimental native Windows launcher and cleanup work must not be confused with the default production runner. See the [development branch status](https://github.com/richardlou12717/MiniAV/blob/feature/m3-clamav-adapter/PROJECT_STATUS.md) for progress and unresolved safety constraints.

> **Security notice:** MiniAV is a prototype, not a replacement for Microsoft Defender or a production antivirus product. The stable scanner only matches harmless local SHA-256 demonstration signatures. `NO_MATCH` does not establish that a file is safe. Do not disable existing endpoint protection.

## Features on `main`

- Read-only, on-demand file and directory scanning using a modular Core / engine design.
- Streaming SHA-256 matching against a local demonstration signature database.
- Command-line interface and Windows Presentation Foundation (WPF) desktop UI.
- User cancellation; configurable file-count, per-file-size, and scan-duration limits.
- Explicit match, no-match, skipped, and error outcomes; incomplete scans are not treated as clean scans.
- Reparse-point / junction skipping and bounded UI buffering and log display.
- Windows regression tests and GitHub Actions CI for CLI/Core and WPF builds.

## Repository layout

| Path | Purpose |
|---|---|
| `src/MiniAV.Core/` | Scan coordination, contracts, limits, and lifecycle handling |
| `src/MiniAV.Engines.Hash/` | Streaming SHA-256 demonstration engine |
| `src/MiniAV.Cli/` | Command-line interface |
| `src/MiniAV.Wpf/` | Desktop interface and scan reporting |
| `tests/MiniAV.Core.Regression/` | Core regression checks |
| `scripts/regression-test.ps1` | Windows regression test runner |
| `.github/workflows/windows-ci.yml` | Automated Windows build and regression checks |

## Run locally (Windows)

Install the .NET 10 SDK and run these commands from the repository root:

```powershell
dotnet run --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json
dotnet run --project .\src\MiniAV.Wpf -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1
```

CLI exit codes: `0` = no demonstration match and no reported error; `1` = demonstration signature match; `2` = invalid arguments; `3` = scan error, limit, or unavailable engine; `130` = user cancellation. A zero exit code is **not** proof that scanned files are harmless.

The WPF interface supports optional limits for file count, file size (MiB), and scan duration. Cancelled or limited scans are marked as incomplete. The UI keeps only a bounded log; if truncated, an exported report is labeled `PARTIAL DISPLAY LOG`, not a complete audit trail.

## Safety and project status

- Scanning is read-only: no automatic deletion, quarantine, file movement, or execution of scanned files; no kernel driver.
- No automatic file/path upload and no automatic persistent scan-log recording.
- Real malware detection coverage, signature freshness, software signing, and production-ready security assurances are **not** established.
- The experimental ClamAV integration is confined to a separate branch and remains subject to further safety validation.

For design details, see the [architecture](docs/02_ARCHITECTURE.md), [security model](docs/03_SECURITY_MODEL.md), and [roadmap](docs/04_ROADMAP.md). The [project status](PROJECT_STATUS.md) documents the stable branch.
