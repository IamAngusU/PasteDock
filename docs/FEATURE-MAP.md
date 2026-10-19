# Feature map

Use this before opening a feature task. The machine-readable presence report is `feature-presence.json`; competitor context is in `PRODUCT-AUDIT.md`. Before adding a command or service, run `python scripts/audit-features.py --find "feature idea"` and inspect any matching implementation paths first. The fuzzy result is a planning guard, not a runtime claim.

## History and retrieval

Independent encrypted local history, deduplication/copy counts, pins, favorites, tags, source app metadata, instant search, deep search, app/type/tag/date/state filters, full-payload preview loading and virtualized history rows are implemented.

## Paste workflows

Original-format paste, plain-text paste, multi-selection composition, FIFO queue, `Alt+1...9` quick paste, paste-as-temporary-file and per-target plain-text rules are implemented. Paste injection goes through the existing guarded `PasteService` path.

## Developer tools

Inline snippet editing, JSON/line/case/URL/Base64 transforms, native two-clip text diff, `.diff` export and conservative code-file extension inference are implemented. Arbitrary script execution is intentionally absent.

## Images and files

PNG storage, Windows bitmap fallback, image canvas, local OCR, PNG export and file-reference capture are implemented. PasteDock does not archive the content of referenced files.

## Privacy and recovery

DPAPI CurrentUser storage, sensitive clipboard marker checks, source exclusions, recognizable-secret filtering, capture pause/session lock, source-wide delete review, encrypted delete undo and passphrase-protected portable backup are implemented. Temporary paste-as-file output is the explicit cleartext exception documented in `SECURITY.md`.
