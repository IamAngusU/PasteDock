# PasteDock 0.4.0 validation

Executed on **19 October 2026**, on Windows x64 with .NET SDK **10.0.401**. These are release checks by the maintainer, not an independent audit. GitHub Actions repeats core tests on Linux and builds Windows x64/ARM64; its live result is separate from this local snapshot.

- **134/134 core regression tests passed.** Storage transactions, tampering, quota refusal, cancellation, undo durability, backup authentication, import atomicity, search, transforms, bounded diff and missing file references. [Executed JSON](validation/core-windows-x64.json).
- **16/16 Python tooling tests passed.** Includes a Windows text-decoding fix in the source checker. The static validator and 14-item feature-presence audit also passed. Static checks alone do not establish runtime correctness.
- **Windows solution build passed with zero warnings and zero errors.** WPF's manifest owns per-monitor DPI; the WinForms-only DPI analyzer is explicitly suppressed because WinForms is used for tray and monitor bounds.
- **16 WPF UI scenario groups passed**, with no binding errors. Light/dark settings and workspace, diff, undo, inline editing, empty/secret snippet rejection, invalid settings, invalid JSON, cancelled searches, no-results recovery, deleted queue entries, preserved drafts, session lock and a 720 × 560 layout. [Executed JSON](validation/ui-windows-x64.json).
- **14 simulated paste scenarios passed**, exercising the actual paste coordinator through fake clipboard/desktop boundaries. Missing or reused target, activation refusal, held modifiers, changed focus, locked/secure desktop, newer clipboard sequence, blocked or partial input, caller cancellation, timeout recovery, concurrent requests and success. Native checks also passed for DPAPI round-trip/tamper rejection, Windows INPUT layout, clipboard sequence API and paused STA listener registration. [Executed JSON](validation/native-windows-x64.json).

The diagnostics do not overwrite the user's clipboard or inject real keystrokes. UI checks use a temporary synthetic workspace and remove it afterward. Simulated paste checks test the coordinator, not the behavior of every third-party target application.

## Fixed from the supplied 0.3.0 source

The first baseline run had **125/127** passing core tests and crashed in the actual WPF view. A `Run.Text` binding attempted to write into the read-only QuickKey property. The unhandled-error dialog recursively re-entered failing layout and overflowed the stack. The release makes the binding one-way, prevents recursive error handling and writes diagnostic failures without modal dialogs.

Two Windows-specific failures were also resolved: diff exports now use deterministic LF boundaries, and the export staging test accepts Windows' actual access-denied result for a directory destination. The assertion still checks that the original directory survives and staging files are removed.

Visual inspection caught inherited font/foreground problems that made icon buttons unreadable and dark-mode titles black. Control inheritance, theme brushes and the high-resolution mark were corrected, then the real views were rendered again.

## Remaining acceptance limits

The **x64** build has been executed locally. The **ARM64** build is cross-compiled; it has not been run on native ARM hardware. CI records skipped native checks explicitly for a mismatched host architecture.

Before a stable release, manually exercise actual paste into several target apps, an elevated target, remote desktop, clipboard ownership contention, multiple monitors at 100/150/200% scaling, sleep/resume and the desired OCR languages. The tests cover coordinator decisions and synthetic WPF state; they are not a substitute for that native desktop acceptance.

Packages are **unsigned preview builds**. SHA-256 files detect a mismatched download; they are not publisher signatures. The app has no automatic updater. Pins/favorites may fill a quota, file clips may outlive their sources and DPAPI still depends on the original Windows profile. See [privacy and recovery limits](SECURITY.md).
