# MiniAV — Project Charter

- **Started:** October 17, 2026
- **Ownership:** Personal development project; design and implementation are developed collaboratively with AI assistance, with local Windows verification by the project owner.
- **Goal:** Explore safe, local, on-demand file scanning on Windows.
- **Technology:** C# / .NET 10; a read-only modular scanning framework, local hash engine, and an optional external scanner adapter. CLI first, followed by WPF.

## Design principles

Least privilege; read-only by default; separate detection from remediation; explicit failure reporting; traceability; privacy by default; and testable resource and lifecycle limits.

## Outside the current supported scope

Kernel drivers, real-time protection, cloud submission, automatic file deletion, malware repair, and replacing Microsoft Defender.

Quarantine and restoration were considered as possible future features but are **not implemented on stable `main`**.
