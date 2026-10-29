# MiniAV — Roadmap

| Milestone | Scope | Status |
|---|---|---|
| M0 | Project skeleton, documentation, read-only CLI prototype | Completed |
| M1 | Modular Core / Hash engine, CLI, cancellation/error handling, Windows regressions and CI | Completed; PR #1 merged |
| M2 | WPF GUI, reporting, cancellation, resource limits, bounded UI logs, regressions | Completed; PR #2 merged |
| M2.1 | Explicitly authorized quarantine and reversible restoration | Not implemented; separate safety design required |
| M3 | Optional out-of-process ClamAV adapter and related safety analysis | In progress on feature branch; Draft PR #3 unmerged |
| M4 | Concurrency, incremental caching, further performance improvements | Future |

## M3 integration gates

The feature branch explores real and simulated external-process tests, safe process arguments, bounded output, cancellation, timeout behavior, and Windows native process cleanup. This experimental work is **not merged into stable `main`**.

Before any production runner replacement or merge, unresolved native failure and resource-lifetime risks must be addressed, followed by explicit review and Windows CLI/WPF integration acceptance. See the [feature-branch project status](https://github.com/richardlou12717/MiniAV/blob/feature/m3-clamav-adapter/PROJECT_STATUS.md).

## Security disclaimer

Only harmless demonstration signatures are supported in the stable scanner. `NO_MATCH` is not proof of safety. Do not substitute MiniAV for Microsoft Defender. No automatic quarantine, deletion, movement, or execution of scanned files.
