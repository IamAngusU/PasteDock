# PasteDock 0.3.0 diff audit

Baseline: the committed 0.2.0 source tree in this package's development history. This file records why each 0.3 change exists so future work can distinguish a genuine product gap from a renamed duplicate.

## Added because the capability was absent

| Area | Added files / primary change | Reason |
|---|---|---|
| Text comparison | `TextDiff.cs`, `ShellViewModel.Compare.cs` | No native two-history-item comparison existed. |
| Quick paste | `ClipItemViewModel.cs`, `MainWindow.xaml.cs` | History navigation existed, but no direct first-nine result keys existed. |
| Paste as file | `TemporaryPasteFileService.cs`, `TextFileFormatGuesser.cs` | Export existed, but not a temporary file-drop paste workflow. |
| Search | `SearchQuery.cs`, `SearchMatcher.cs` | Existing parser lacked protected/OCR/copy-count/before filters. |
| Source privacy | `ShellViewModel.Power.cs` | App exclusions existed only in Settings, not as a contextual action from an offending source. |
| Target behaviour | `AppSettings.cs`, `SettingsViewModel.cs`, `ShellViewModel.Power.cs` | No persisted target-specific formatting rule existed. |
| UI | `MainWindow.xaml` | Exposes the new actions without introducing a new modal or separate management window. |
| Regression coverage | `V030Tests.cs`, source validator guards | New parser, diff bounds, extension inference and settings normalization need regression anchors. |

## Explicitly reused instead of duplicated

- Existing `PasteService` remains the only SendInput path.
- Existing `WindowsClipboardService` remains the only live clipboard write path.
- Existing `HistoryRepository.LoadAsync` remains the payload source for compare/quick-paste/file-paste.
- Existing `ExportService` performs temporary file writes, so paste-as-file inherits atomic staging rather than growing a second file writer.
- Existing settings persistence is extended with one normalized target-app list rather than adding a second config file.
- Existing delete confirmation and encrypted undo are reused for whole-source deletion.
- Existing popovers and the command menu are reused. No modal framework was added.

## Behavioural boundaries

- `Alt+1...9` operates on the currently visible, filtered ordering, not raw storage order.
- Diff never writes either source clip. Maximum one million characters and 20,000 lines per side; Myers edit distance is capped at 512 before safe fallback.
- Paste-as-file necessarily creates an unencrypted cleartext file because the receiving application needs a normal file path. It is local, marked Temporary/NotContentIndexed, and eligible for cleanup after 24 hours from its PasteDock UTC filename timestamp; cleanup runs only while PasteDock starts/runs.
- Source exclusion affects future capture only. Existing history remains until the separate delete action is confirmed.
- Target plain-text rules only flatten content that is text-capable. They do not reinterpret a normal image or file selection as text.

`python scripts/audit-features.py` produces `docs/feature-presence.json`. It checks source anchors only and must never be described as runtime validation.
