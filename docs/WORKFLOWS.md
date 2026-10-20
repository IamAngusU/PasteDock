# Everyday workflows

## Name something you will reuse

Select one clip and press **F2**, or choose its rename button. A name has 1–150 characters on one line. The name is separate from the payload: copying and exporting still use complete original content. A custom name survives editing, repeat capture, undo and portable backup.

Pin a captured clip if you want to keep it through automatic retention. Renaming alone does not pin it. New snippets created inside PasteDock belong to **My snippets** and are protected from automatic retention. This identity survives copying the same content from another app. Protected items can fill the quota; PasteDock asks you to remove unused items or increase limits rather than deleting them silently.

## Assemble a paste queue

1. Select clips using Ctrl/Shift and choose **Add to queue**, or press **Ctrl+Q**.
2. Expand the queue. Select a row, use the up/down buttons to reorder it, or remove that occurrence.
3. Choose **Paste next**, or press **Ctrl+Alt+N** in your target app. It requests exactly one paste.
4. For apps that need manual paste, choose **Copy & advance**, then use Ctrl+V yourself.

Repeated clips are intentional separate queue entries. The queue uses a distinct ticket for each occurrence. A stale completion cannot remove a different head after reordering. Clipboard failure or an uncertain paste does not advance the entry. Missing clips are skipped with an explanation. Copy & advance only advances after a verified clipboard write; it cannot confirm that you subsequently pasted manually.

The queue is session state, not a saved collection. Windows lock clears it; quitting clears it. The underlying encrypted clips remain. Hiding the window keeps the queue.

## Search without fighting the filters

Words are combined with AND. A minus sign excludes a term. Quotes keep phrases together and let you search text that looks like an operator.

    release -draft
    "release notes" -"old version"
    app:"Visual Studio" -#archive
    type:json -app:Browser
    is:snippet copies:3
    is:pinned -type:image
    "app:literal text"
    after:2026-01-01 before:2026-02-01

Negative app, tag, type and flag filters are supported. Examples: -app:Browser, -#archive, -type:image, -is:pinned, -is:snippet, -has:ocr. A quoted "-draft" searches for that literal text. Unknown or incomplete operators remain searchable text.

The instant index contains up to 4,096 characters per entry. Turn on **Deep** to search complete stored text, OCR text and file paths. Deep also inspects negative terms beyond the index, even when the positive words already match the title. Metadata such as names, source apps and tags remains searchable.

Choose a filter from the toolbar or sidebar. The toolbar stays available when a narrow window hides the sidebar. Sort by relevance, copy recency, copy count, creation time or complete payload size. Pins stay first in each order. Sort and wrap preferences are saved when settings are saved or the app exits normally.

## Keep a draft safe

Hiding the window preserves an unfinished draft. Cancelling changed text opens an inline choice: keep editing or discard changes. Quit asks you to finish or cancel the edit first. Windows lock hides the window and clears previews while retaining the draft in RAM.

Changed editor text gets one encrypted local recovery copy after one second. The status distinguishes waiting, successful encryption, privacy rejection, size limits and storage failure. Changes before the timer completes, or after a reported failure, are not guaranteed to survive a crash. Save your snippet for durable history.

At the next launch, choose **Restore draft** or review **Discard...**. New edits stay disabled until you resolve the copy. Unreadable recovery stays in place with retry and explicit discard actions. A draft whose original changed or disappeared is restored as a new snippet. Active editors also verify the original payload revision before replacing it.

Recovery holds one draft of up to 1,048,576 characters, expiring seven days after its last saved revision. Expiry and current privacy rules are checked at startup, on restore and during hourly maintenance. Rejected or blank revisions remove their earlier recovery copy. Drafts are excluded from portable backups and the ordinary clip quota; the encrypted file is additionally capped at 8 MiB.

Turn **Recover editor drafts after restart** off in Settings to remove the disk copy and retain the live editor. Failed removal is reported for retry. Cancellation closes the editor only after removing recovery. If history saves but cleanup fails, PasteDock reports the successful save and offers cleanup separately.

## Keep the original while editing

**Ctrl+Enter** saves changes to the clip you opened. **Save as new** or **Ctrl+Shift+Enter** creates a separate protected snippet, including when its text matches another clip. Stable snippet identities survive backup import, re-copying and edits. Reimporting an existing identity skips it and never replaces a newer version. Older snippets migrate using their original clip identity.

Manual new-snippet saves refuse to exceed the item or content limit without evicting other clips. Failed saves leave your editor open. Successful new snippets are selected in **My snippets**.

## Inspect content

The text preview can wrap or scroll horizontally; both modes keep the original content unchanged. Text tools, export and queue actions are directly visible below the preview. Colors use CSS hex order (#RGB, #RGBA, #RRGGBB, #RRGGBBAA); the last component is alpha.

Images retain their original PNG bytes. Inspection decodes a bounded preview; local OCR still needs installed Windows recognition languages. File clips are references. Missing sources are rejected before replacing the clipboard; use plain text if you only need their paths.

Editing stores plain text. When a clip also contains HTML, RTF, image data or file references, the editor explains that saving changes replaces those formats. Use Save as new to preserve the original. After a successful save, PasteDock clears the previous query and reveals the result so it cannot disappear behind an outdated search filter.
