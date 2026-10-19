# Product audit, 7 October 2026

This audit exists to stop PasteDock from growing by feature duplication. Before a new capability is added, check `docs/feature-presence.json`, the source, and this matrix. A checked source anchor is only evidence that an implementation exists, not proof that it works at runtime.

For planning, run `python scripts/audit-features.py --find "proposed feature"`. The result is deliberately heuristic: a match means **inspect/reuse this path first**, not that the requested behavior is complete or runtime-validated. CI separately requires every declared feature anchor to remain present.

## What the established Windows options do well

| Product | Strong points verified in official material | PasteDock response |
|---|---|---|
| Windows clipboard history | Built in, pins and cross-device sync. It is limited to 25 entries, 4 MB per item, and Text/HTML/Bitmap history formats. | Keep an independent history with larger configurable limits, explicit file references, local encryption and no dependency on Win+V history. |
| PowerToys Advanced Paste | Plain text, Markdown, JSON, paste as `.txt`/`.html`/`.png`, local OCR, media transcoding, optional local or online AI, custom AI actions and access to clipboard history from its window. | Keep OCR and deterministic transforms local. v0.3 adds paste-as-file with conservative developer extensions while retaining PasteDock's own independent encrypted history, deep search and clip metadata. Media transcoding and AI are not required for the clipboard core. |
| CopyQ | Search/filter, tabs, tags/notes, custom formats, global shortcuts, command line, scripting and ignore rules. | Keep fast filtering/tags and app exclusions. Do not put arbitrary script execution in the clipboard capture/paste path. Add deterministic target rules instead. |
| Ditto | Very fast Windows-first history, broad clipboard formats, simple hotkey-to-paste workflow and local-first/no-login positioning. Its current change history also includes external Visual Studio Code comparison support and plain-text position shortcuts. | Preserve a fast keyboard path, but keep comparison inside the app: v0.3 adds visible `Alt+1...9` direct paste and a native bounded two-clip diff without requiring an external editor. |
| PasteBar | Unlimited searchable history, custom clips, collections/tabs/boards, quick menus, local storage, lock/passcode, templates/forms and broader automation. | Pins/favorites/tags remain deliberately lighter than boards. Native compare, source privacy actions and deterministic target paste rules focus PasteDock on high-signal workflows rather than becoming a general automation platform. |
| ClipboardFusion | C# macros, event/filter triggers, hotkeys, clipboard manager and fast access to its first items. | v0.3 adopts the useful ideas of app-specific behaviour and numbered fast access as constrained target rules plus `Alt+1...9`, without arbitrary code execution in the capture/paste path. |

Official references reviewed:

- Microsoft Windows clipboard: https://support.microsoft.com/windows/apps/using-the-clipboard
- Microsoft PowerToys Advanced Paste: https://learn.microsoft.com/windows/powertoys/advanced-paste
- CopyQ: https://hluk.github.io/CopyQ/
- Ditto: https://ditto-cp.sourceforge.io/
- PasteBar: https://www.pastebar.app/
- ClipboardFusion: https://www.clipboardfusion.com/Features/

## Diff check before v0.3

The following requests were already present in 0.2 and were **not** reimplemented:

| Capability | Existing implementation |
|---|---|
| Type-to-search | `MainWindow.Reveal()` focuses and selects the search field. |
| Local OCR | `WindowsOcrService`, opt-in extraction from image clips. |
| Multi-select | Native extended history selection plus bounded multi-payload composition. |
| Paste queue | FIFO queue with explicit global `Ctrl+Alt+N`; no unsafe auto-run through every item. |
| Plain-text paste | `Shift+Enter` and command menu path. |
| App exclusions | Capture policy with normalized process-name exclusions. |
| Tags/pins/favorites | Existing metadata and filters. |
| Local text transformations | JSON, lines, case, URL and Base64 operations. |
| Deep search | Full encrypted payload search beyond the 4,096-character instant index. |
| Encrypted backup | AES-256-GCM archive with password-derived key. |
| Command menu | Existing keyboard-first command surface. |

## Gaps implemented in v0.3

1. **Native two-clip diff.** Exactly two text-capable clips can be compared without exporting them or running a script. Normal edits use bounded Myers line diff; large edit distances switch to a deterministic replace-block fallback so comparison work stays bounded.
2. **Direct result paste.** The first nine visible results show `Alt 1...9` keycaps and can be pasted without changing selection first.
3. **Paste as file.** Text/code/image clips can be materialized as short-lived local files. Developer text gets a conservative extension such as `.cs`, `.ts`, `.py`, `.sql` or `.ps1`; ambiguous content remains `.txt`.
4. **Power search operators.** Added `is:protected`, `has:ocr`, `copies:N` and `before:YYYY-MM-DD` on top of the existing operators.
5. **Source privacy actions.** From a selected clip, exclude its source process from future capture or review deletion of every stored clip from that source.
6. **Per-target plain-text rules.** A target process can be configured to receive plain text automatically for text-capable clips, including quick paste and queue paste. Files and images without OCR retain their original format.
7. **Visual diff inspector.** Added/removed lines get restrained local highlighting; originals are never modified.

## Deliberate non-goals

- No cloud account or clipboard sync in the core app.
- No arbitrary C#/JavaScript/Python macro execution from captured content.
- No automatic URL fetch, web scraping or HTML execution from clipboard entries.
- No hidden AI request. If AI is ever added, it should be an explicit optional tool with a visible data boundary.
- No promise of "unlimited" storage. Explicit quotas are easier to reason about and safer under damaged or hostile clipboard providers.

## Next high-value gaps

These are intentionally not disguised as completed work: protected collections with a separate unlock boundary, richer reusable templates with typed fields, and native Windows accessibility/keyboard acceptance on real hardware. They should only be scheduled after 0.3 compiles and its Windows smoke/acceptance suite is green.
