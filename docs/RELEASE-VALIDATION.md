# PasteDock 0.6.0 validation

Executed on **20 October 2026**, Windows x64, with the .NET SDK selected by global.json. This is maintainer testing, not an independent audit.

- **197/197 core regression tests passed.** Thirty additions cover encrypted recovery, expiry boundaries, current privacy rules, cancellation, tampering, invalid/oversized files, injected write failure, serialized autosave/settings changes, independent identical snippets, idempotent import, deletion undo, legacy snippet identities, optimistic edit conflicts and quota preservation. [Executed core report](validation/core-windows-x64.json).
- **16/16 Python tooling tests passed.** Source structure validation and the feature audit also pass. Static checks do not establish runtime behavior.
- **38 WPF scenario groups** cover the real views in isolated synthetic storage. Thirteen additions exercise the actual autosave timer, a new shell over persisted recovery, privacy rejection, in-flight discard, restore/save cleanup, identical Save as new, stale edits, missing originals, unreadable recovery, locked-file discard/cleanup, rich-format preservation, opt-out and dark/720-pixel editor layout. Existing queue, search, image, color, palette, settings, lock and scrolling checks remain. [Executed UI report](validation/ui-windows-x64.json).
- **14 simulated paste scenarios** exercise the real paste coordinator through fake OS boundaries. Native checks also exercise DPAPI round-trip/tamper rejection, INPUT layout, clipboard sequence API and a paused STA listener. [Executed native report](validation/native-windows-x64.json).
- The Windows solution compiles with zero warnings and zero errors. SHA-pinned GitHub Actions are configured for Linux core tests and Windows x64/ARM64 packaging.

Diagnostics do not replace the user's clipboard or inject real keystrokes. WPF screenshots are actual application renders with synthetic clips. Queue-copy failure is tested using an unavailable clipboard service, so it cannot write real content. Simulated paste tests establish coordinator decisions, not compatibility with every target app.

## Compatibility and storage

The encrypted history schema stays at version 2. Stable snippet identities are additive; older snippets use their original clip identity for migration. Intentional equal-content snippets remain independent through backup import and undo. Reimporting an existing snippet identity keeps its current version. Older PasteDock versions lack these semantics; use the current version for editing/import and retain a backup before downgrading.

The queue stays in memory and clears on Windows lock or quitting. Editor recovery uses a separate current-user DPAPI file, capped at 1,048,576 text characters and 8 MiB encrypted storage. The timer saves after one second; the status must show success before crash recovery can be expected. Privacy rejection and blank revisions remove the prior copy. Seven-day expiry is checked on startup, restore and hourly maintenance. Opt-out removes the disk copy while retaining the live editor. Drafts are excluded from portable backups and history quotas. Existing complete-content, DPAPI and backup limits remain in force.

## Hosted CI and remaining acceptance

The previous release's hosted runs stopped before job creation with startup_failure. A hosted pass is not inferred from local results. See the [current workflow](https://github.com/IamAngusU/PasteDock/actions/workflows/windows.yml) for the latest status. If account execution remains unavailable, the release is supported by the attached local reports only.

**x64** is executed locally. **ARM64** is cross-compiled and has not been run on native ARM hardware. Native/UI checks are explicitly skipped on mismatched build hosts. Both packages are unsigned previews.

Full manual acceptance remains outstanding for actual paste across third-party apps, elevated targets, remote desktop, clipboard contention, sleep/resume and mixed-monitor scaling. Automated recovery checks reconstruct the shell over encrypted storage; they do not simulate power loss at every CPU instruction. SHA-256 checks integrity; it is not a publisher signature. [Privacy and recovery model](SECURITY.md). [0.5.0 validation snapshot](https://github.com/IamAngusU/PasteDock/blob/v0.5.0/docs/RELEASE-VALIDATION.md).
