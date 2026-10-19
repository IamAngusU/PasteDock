<p align="center"><img src="docs/assets/pastedock-wordmark.svg" width="390" alt="PasteDock — Keep your flow."></p>
<p align="center"><a href="README.de.md"><img src="docs/assets/language-de.svg" height="40" alt="Diese README auf Deutsch lesen"></a></p>
<h1 align="center">A home for everything you copy.</h1>
<p align="center">Find it. Fix it. Paste it again.<br>A private clipboard workspace built for Windows.</p>
<p align="center">
  <a href="https://github.com/IamAngusU/PasteDock/releases/tag/v0.4.0"><img src="docs/assets/badge-release.svg" height="40" alt="0.4.0 preview"></a>
  <a href="#get-started"><img src="docs/assets/badge-platforms.svg" height="40" alt="Windows x64 and ARM64"></a>
  <a href="#your-clipboard-stays-yours"><img src="docs/assets/badge-privacy.svg" height="40" alt="Local and encrypted"></a>
  <a href="docs/RELEASE-VALIDATION.md"><img src="docs/assets/badge-tests.svg" height="40" alt="134 executed core regression tests"></a>
</p>
<p align="center"><a href="https://github.com/IamAngusU/PasteDock/actions/workflows/windows.yml"><img src="https://github.com/IamAngusU/PasteDock/actions/workflows/windows.yml/badge.svg" alt="Current GitHub validation status"></a></p>
<p align="center"><a href="#get-started">Download</a> · <a href="#make-copied-things-useful">Features</a> · <a href="#your-clipboard-stays-yours">Privacy</a> · <a href="docs/RELEASE-VALIDATION.md">Test evidence</a> · <a href="CONTRIBUTING.md">Contribute</a></p>

![PasteDock's real WPF workspace](docs/assets/workspace-light.png)
<sub>Actual Windows rendering with synthetic clips. Light and dark themes, keyboard navigation and a resizable inspector.</sub>

## Get started

1. Download **PasteDock-0.4.0-win-x64.zip** from the [0.4.0 preview release](https://github.com/IamAngusU/PasteDock/releases/tag/v0.4.0). Use the ARM64 archive on a Windows ARM device.
2. Extract the entire folder and run **PasteDock.exe**. The self-contained build includes its .NET runtime. No administrator account or sign-in is needed.
3. Copy something in another app. Press **Ctrl+Alt+V** to find it, then **Enter** to paste. **Shift+Enter** pastes plain text.

Closing the window keeps capture running in the tray. **Quit PasteDock** in the tray stops the app. Autostart is opt-in under Settings. The preview builds are unsigned; Windows may show an unknown-publisher prompt.

Windows 10 version 2004 or later is the technical API minimum; use a Windows version that still receives security updates. x64 is executed and tested locally. ARM64 is cross-compiled; native ARM64 acceptance remains outstanding.

<details>
<summary>Build from source</summary>

Install the .NET 10 SDK selected in `global.json`, then run:

```powershell
.\scripts\test.ps1
.\scripts\build.ps1 -Runtime win-x64
# ARM64 package
.\scripts\build.ps1 -Runtime win-arm64
```

`build.cmd` can bootstrap a project-local SDK from hash-checked Microsoft downloads. `artifacts/last-successful-build.json` points to the package and reports. The app itself makes no network requests.

</details>

## Make copied things useful

- **Find the whole thing.** Instant search, full-content Deep search, `#tags`, `app:`, `type:`, dates and copy counts. Copy, edit and export always use complete content.
- **Keep what matters.** Pins, favorites, tags, editable snippets and duplicate detection with copy counts.
- **Paste with intent.** Original formats, plain text, per-app plain-text rules, multi-selection, Alt+1–9 and an explicit queue. Every queue step needs your action.
- **Work with text.** Format JSON, normalize lines, sort or deduplicate, convert URL/Base64 and compare two clips without modifying either original.
- **Inspect images.** Zoom, pan, original PNG export and local Windows OCR on request. OCR language packs must already be installed.
- **Recover mistakes.** Ten-minute encrypted deletion undo and passphrase-encrypted portable backups. Imports never evict existing clips to make room.

<details>
<summary>Dark mode and first use</summary>

![Dark theme](docs/assets/workspace-dark.png)
![First-use workspace](docs/assets/first-use.png)

</details>

## Built for the awkward cases

A missing file is reported before replacing the clipboard. Invalid JSON leaves its original intact. Failed settings stay open for correction. Hiding the window preserves an unfinished draft. A deleted queue entry is skipped with an explanation.

Before sending paste keys, PasteDock rechecks the target process, foreground window, desktop state, held modifiers and clipboard sequence. If Windows blocks activation or input, the app tells you to paste manually. A partial synthetic input sequence releases its synthetic modifiers. A concurrent paste is rejected; an uncertain request is not replayed automatically.

These paths have executable checks, including simulated OS failures. See [what passed and what still needs manual testing](docs/RELEASE-VALIDATION.md).

## Your clipboard stays yours

Clipboard content stays in `%LOCALAPPDATA%\PasteDock`, encrypted with current-user Windows DPAPI. There are no accounts, telemetry, cloud sync, AI requests or automatic updates. Password-manager exclusions, private clipboard markers, pause and best-effort recognizable-secret filtering are built in.

DPAPI does not protect against another program running as the same Windows user. Secret detection cannot recognize every password. File clips are references, so moving or deleting the original makes them unavailable. Exports and temporary paste files are ordinary local files. Portable backups use a separate passphrase. [Full privacy model](docs/SECURITY.md).

Defaults: 2,000 clips, 30 days for unprotected history, 512 MiB of logical content. Pins and favorites are protected from retention. Individual clips are limited to 64 MiB; images to 40 million pixels. Oversized copies are skipped explicitly. Backups are limited to 128 MiB of raw content. [Detailed reference](docs/USER-GUIDE.de.md).

## A native app, a small stack

[![PasteDock stack rendered with Icon Marquee](docs/assets/icon-marquee.svg)](https://github.com/IamAngusU/icon-marquee)

C# and WPF on .NET 10. No third-party runtime NuGet packages. The portable core is independent of Windows UI and clipboard APIs. [Architecture](docs/ARCHITECTURE.md).

The repository follows the same presentation family as [ContextBridge](https://github.com/IamAngusU/ContextBridge) and [ByeClaude](https://github.com/IamAngusU/ByeClaude). README assets use the owner's Badges renderer; the animated stack is a self-contained [Icon Marquee](https://github.com/IamAngusU/icon-marquee) export. These are development and documentation tools. PasteDock remains local. [Asset provenance](docs/assets/README.md).

[MIT](LICENSE) · [Changelog](CHANGELOG.md) · [Security policy](SECURITY.md) · [Contributing](CONTRIBUTING.md)
<p align="center"><sub>Made by Angus Uelsmann · <a href="https://angusu.de">angusu.de</a></sub></p>
