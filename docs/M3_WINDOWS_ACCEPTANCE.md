# M3 Windows Acceptance Checklist (not yet signed off)

This checklist is for **local Windows verification** after GitHub CI passes; it is not evidence of tests having been run. Keep PR #3 Draft until all required items are observed and documented.

## Preconditions
- Checkout `feature/m3-clamav-adapter` into a clean working tree.
- Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\regression-test.ps1`.
- Run `dotnet build .\src\MiniAV.Wpf\MiniAV.Wpf.csproj -c Release`.
- Use ordinary harmless files. No malware specimen is required.
- ClamAV is **optional** and should only be installed manually from verified official sources. MiniAV does not fetch binaries or signatures.

## CLI
1. Existing demo command `dotnet run --project src/MiniAV.Cli -- --path tests/fixtures --db signatures/demo-hashes.json`: existing demo results and exit codes unchanged.
2. `dotnet run --project src/MiniAV.Cli -- --path tests/fixtures/ordinary-sample.txt --engine clamav --clamav-exe C:\\invalid\\clamscan.exe`: exit 3, `ERROR`, never `NO_MATCH`.
3. Supply a real, verified absolute path to `clamscan.exe`, and optionally a local, verified ClamAV database directory. Test a harmless ordinary file, a path with spaces, and a Unicode path.
4. Invalid or absent database, unsupported flags, and ClamAV execution failure must not yield exit 0.
5. Run a larger ordinary input and cancel with Ctrl+C; expect 130 and child termination, not a success report.
6. Confirm no target is moved/deleted and Defender remains active.

## WPF
1. Launch with default Hash selection. Browse a demo file/database and confirm previous M2 behavior.
2. Select ClamAV. Hash database controls disable; ClamAV executable and optional database-directory controls enable.
3. Select missing ClamAV executable, scan a harmless target and verify **incomplete/error** status, not successful clean scan.
4. Test real ClamAV only if installed. Confirm matched/no-match/error labels, counts and report metadata (version is explicitly unverified).
5. Start a scan; verify controls lock, cancel works, restart works, and close requests wait until subprocess cleanup.
6. Confirm Save Log is manual and bounded; no automatic logs or uploads are created.
7. Switch back to Hash, verify its controls re-enable and ClamAV inputs disable.

## Security gates before merge
- **Real engine compatibility:** verify actual supported ClamAV version and every CLI flag on Windows.
- **Incomplete coverage:** document limits for archives, encrypted containers, oversized files, parser failures, and `Heuristics.Limits.Exceeded` mapping; no safety guarantee from `NO_MATCH`.
- **Resource isolation:** process timeout + output cap exist; **hard memory/CPU caps and process sandboxing are not yet implemented**.
- **Distribution/licensing:** no ClamAV binary distributed in M3; review before bundling. Signature provenance/freshness/update/rollback are deferred.
- **UI manual tests:** no automated native WPF test exists.
- Document observed command, platform, ClamAV/database version, result, and unresolved limitations in PR #3 before any merge.

## Performance note
The initial coordinator invokes one `clamscan` process per file and reloads the signature database each time. Do **not** present it as practical large-directory real-time protection. A persistent daemon or batch interface requires its own architecture/security review.
