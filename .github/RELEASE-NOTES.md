PasteDock 0.4.0 is a native, local Windows clipboard workspace with encrypted history, complete-content search, snippets, text tools, image inspection, offline OCR, queueing and portable backups.

This preview adds a consistent violet identity, a shared app/tray icon, corrected dark-mode contrast, actionable first-use and no-results states, safer missing-file handling and draft recovery. It fixes the QuickKey binding crash and recursive error-dialog failure in the supplied 0.3.0 source.

Executed local checks: 134 core regression tests, 16 Python tooling tests, 16 WPF scenario groups and 14 simulated paste scenarios, plus native DPAPI/input-layout/listener checks. GitHub CI results are available in the workflow. [Details and remaining acceptance](https://github.com/IamAngusU/PasteDock/blob/main/docs/RELEASE-VALIDATION.md).

Download the ZIP for your architecture, extract the whole folder, and open PasteDock.exe. The runtime is included. Press Ctrl+Alt+V to open the workspace. No administrator account or sign-in is required.

These are **unsigned preview builds**. x64 has been executed locally. ARM64 is cross-compiled and has not yet been accepted on native ARM hardware. Full manual target-app, remote-desktop and multi-monitor acceptance remains outstanding. SHA-256 files are download integrity checks, not publisher signatures.
