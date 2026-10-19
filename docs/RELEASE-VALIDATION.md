# PasteDock 0.5.0 validation

Executed on **19 October 2026**, Windows x64, with the .NET SDK selected by global.json. This is maintainer testing, not an independent audit.

- **167/167 core regression tests passed.** Includes thirty-three additions for search exclusions, quoted operators, deep negative matching, deterministic sorting, queue tickets/reordering/capacity, durable names, backups, snippet identity/retention, CSS alpha and settings recovery. [Executed core report](validation/core-windows-x64.json).
- **16/16 Python tooling tests passed.** Source structure validation and the feature audit also pass. Static checks do not establish runtime behavior.
- **25 WPF scenario groups** cover the real views in isolated synthetic storage: queue edits and failed copy recovery, rename validation, image/color inspectors, scrollbars and text wrapping, snippet filters, view preferences, search help/exclusions, palette availability, draft discard review, plus previous settings, search, deletion, lock and narrow-layout checks. [Executed UI report](validation/ui-windows-x64.json).
- **14 simulated paste scenarios** exercise the real paste coordinator through fake OS boundaries. Native checks also exercise DPAPI round-trip/tamper rejection, INPUT layout, clipboard sequence API and a paused STA listener. [Executed native report](validation/native-windows-x64.json).
- The Windows solution compiles with zero warnings and zero errors. SHA-pinned GitHub Actions are configured for Linux core tests and Windows x64/ARM64 packaging.

Diagnostics do not replace the user's clipboard or inject real keystrokes. WPF screenshots are actual application renders with synthetic clips. Queue-copy failure is tested using an unavailable clipboard service, so it cannot write real content. Simulated paste tests establish coordinator decisions, not compatibility with every target app.

## Compatibility and storage

The encrypted history schema stays at version 2. New custom-name and snippet flags are additive. Legacy clips from the exact PasteDock source are treated as saved snippets and are protected from retention. Names, snippet identity and view preferences are exercised through encrypted storage and backup round-trips.

The queue is in memory. Windows lock or quitting clears it. Draft discard review protects ordinary cancellation; drafts are not crash-recovery autosaves. Existing complete-content, DPAPI, quota and backup limits remain in force.

## Hosted CI and remaining acceptance

The previous release's hosted runs stopped before job creation with startup_failure. A hosted pass is not inferred from local results. See the [current workflow](https://github.com/IamAngusU/PasteDock/actions/workflows/windows.yml) for the latest status. If account execution remains unavailable, the release is supported by the attached local reports only.

**x64** is executed locally. **ARM64** is cross-compiled and has not been run on native ARM hardware. Native/UI checks are explicitly skipped on mismatched build hosts. Both packages are unsigned previews.

Full manual acceptance remains outstanding for actual paste across third-party apps, elevated targets, remote desktop, clipboard contention, sleep/resume and mixed-monitor scaling. SHA-256 checks integrity; it is not a publisher signature. [Privacy and recovery model](SECURITY.md). [0.4.0 validation snapshot](https://github.com/IamAngusU/PasteDock/blob/v0.4.0/docs/RELEASE-VALIDATION.md).
