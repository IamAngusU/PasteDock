# Start with PasteDock

Download the ZIP for your CPU from the [0.5.0 preview](https://github.com/IamAngusU/PasteDock/releases/tag/v0.5.0), extract the whole folder, and open **PasteDock.exe**. No setup wizard, admin rights, account or separate runtime is needed for the self-contained package. Builds are unsigned previews.

Copy in any app, then press **Ctrl+Alt+V**. Search, choose a clip, press **Enter**. Use **Shift+Enter** for plain text. Press the down arrow from search to navigate clips. **Ctrl+K** opens commands. **Ctrl+,** opens settings.

Closing the window keeps PasteDock in the tray. Choose **Quit PasteDock** to stop capture. Autostart is off until you enable it. A conflicting hotkey leaves the tray and settings available.

History is encrypted in `%LOCALAPPDATA%\PasteDock`. Keep the original Windows profile or create a passphrase-encrypted backup before moving devices. An exported file is not encrypted. File clips reference the original files. Removing the application folder does not remove history; deleting the data folder permanently removes local history and settings, so back up anything you need first.

[English guide](README.md) · [Deutsche Anleitung](README.de.md) · [Validation](docs/RELEASE-VALIDATION.md) · [Privacy](docs/SECURITY.md)

## Developers

Use the .NET SDK selected by `global.json`. Run `scripts/test.ps1` and `scripts/build.ps1`. `build.cmd` can bootstrap a hash-checked SDK into `.tools` without installing it globally. Build outputs and reports go into `artifacts`; the app stays offline.
