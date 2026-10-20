# Changelog

## 0.6.0 — 2026-10-20

- Added bounded encrypted editor recovery: one-second autosave, seven-day expiry, current privacy checks, opt-out, explicit restore/discard and readable failure states. Drafts stay out of portable backups.
- Added Save as new and Ctrl+Shift+Enter, payload revision checks and safe recovery when the original changed or disappeared. New snippets are selected directly.
- Preserved intentional identical snippets through backup import, repeated imports and deletion undo using stable identities. Imports keep current versions of an existing snippet.
- Prevented manual snippet saves from evicting unrelated history. Failed saves/discard retain text; failed recovery cleanup reports already committed history accurately.
- Added editor wrapping, character counts and recovery status. Fixed disabled history backgrounds in dark mode and durable custom names through deletion undo.
- Expanded verification to 197 core tests and 38 WPF scenario groups, alongside 16 Python tests and native/paste-coordinator checks. Builds remain unsigned previews.

## 0.5.0 — 2026-10-19

- Replaced the queue counter with an editable queue: ordered entries, individual removal, move controls, separate tickets for duplicate clips and Copy & advance. Failed operations retain the head.
- Added durable custom clip names and F2 rename, with validation and recognizable-secret filtering.
- Added a protected snippet library. Snippet identity survives re-copy, editing and backup import; snippets are protected from automatic retention.
- Added negative terms/phrases/apps/tags/types/flags, literal quoted operators, correct full-payload negative matching and five deterministic sort orders.
- Kept filters reachable in compact layouts; saved sort and wrap preferences with window settings.
- Added real CSS color inspection, preview wrapping, direct text/queue/export actions, a full-width error banner and explicit draft-discard review.
- Added arrow navigation and availability handling to the command palette, themed both scrollbar directions and waited for quick-paste number keys to be released.
- Expanded local verification to 167 core tests, 25 WPF scenario groups, 16 Python tests and the existing native/paste-coordinator checks.
- Kept the runtime local and dependency-free. Unsigned x64 and cross-compiled ARM64 preview packages include source provenance and validation reports.


## 0.4.0 — 2026-10-19

- Reworked the native workspace with a violet identity, consistent executable/tray icon, readable icon buttons, corrected dark-mode contrast and actionable first-use, paused and no-results states.
- Fixed the read-only QuickKey WPF binding crash and recursive error-dialog failure. Diagnostic runs now fail with a report instead of opening a modal dialog.
- Added file-reference availability checks before clipboard writes, blank-snippet validation, a bounded paste queue and locked-session guards for saving drafts.
- Kept invalid settings and snippets editable, with visible error status. Hiding and locking preserve drafts.
- Made diff exports use portable LF. Fixed Windows-specific export-test expectations and source-checker UTF-8 decoding.
- Added seven core regressions, expanded real WPF coverage to sixteen scenario groups and introduced fourteen simulated paste-coordinator scenarios. Local results: 134 core tests, sixteen Python tests, Windows native checks and UI smoke pass.
- Added bilingual documentation, actual synthetic WPF screenshots, owner-generated Badges assets, Icon Marquee, MIT licensing, issue templates and pinned CI actions.
- Ships unsigned portable preview builds for Windows x64 and cross-compiled ARM64. Native ARM64 and full manual desktop acceptance remain outstanding.

## 0.3.0 · 7. Oktober 2026

### Produkt-Diff statt Feature-Duplikate

Vor der Implementierung wurde die 0.2-Codebasis gegen die gewünschten Workflows und aktuelle Windows-Konkurrenz geprüft. Bereits vorhandene Funktionen wie Type-to-search, lokale OCR, Mehrfachauswahl, Paste-Queue, App-Ausschlüsse, Deep Search, Texttransformationen und verschlüsselte Backups wurden wiederverwendet statt neu gebaut. `docs/PRODUCT-AUDIT.md`, `docs/V030-DIFF-AUDIT.md` und `scripts/audit-features.py` halten diesen Zustand nachvollziehbar.

### Neue Workflows

- Nativer Vergleich von genau zwei textfähigen Clips mit farblich zurückhaltender Line-Diff-Ansicht, Whitespace-Modus, Copy und `.diff`-Export. Myers ist auf eine Edit-Distance von 512 begrenzt; größere Änderungen fallen auf einen sicheren Blockvergleich zurück.
- `Alt+1...9` fügt einen der ersten neun aktuell sichtbaren Treffer direkt ein. Die Keycaps stehen in den Zeilen, damit der Shortcut sichtbar bleibt.
- Paste-as-File erzeugt lokale kurzlebige `.png`, `.json` oder Textdateien. Code bekommt nur bei hoher Heuristik-Sicherheit eine Endung wie `.cs`, `.ts`, `.py`, `.sql`, `.ps1`, `.php`, `.sh`, `.html`, `.css` oder `.xml`; sonst `.txt`.
- Neue Suche: `is:protected`, `has:ocr`, `copies:N`, `before:YYYY-MM-DD`.
- Kontextmenüaktionen können die Quell-App zukünftiger Captures direkt ausschließen oder den gesamten vorhandenen Verlauf dieser Quelle zur bestätigten Löschung vormerken. Die bestehende verschlüsselte Undo-Logik wird wiederverwendet.
- Pro Ziel-App kann eine Plain-Text-Regel gespeichert werden. Sie greift auch bei Quick Paste und Queue, aber nur auf textfähige Clips. Dateien und Bilder ohne OCR behalten ihr Originalformat.

### Datenschutz

Temporäre Paste-Dateien liegen unter dem lokalen PasteDock-Benutzerordner, werden als `Temporary` und `NotContentIndexed` markiert und 24 Stunden nach dem eigenen UTC-Erzeugungszeitpunkt zur Bereinigung freigegeben. Bereinigt wird bei laufender App beziehungsweise beim nächsten Start. Sie sind absichtlich **nicht verschlüsselt**, weil das Zielprogramm einen normalen Dateipfad lesen können muss.

### Prüfung

Der WPF-Smoke rendert in 0.3 zusätzlich den echten Compare-Popover mit synthetischen Clips als separates Bild; Popups werden dabei über ihr eigenes Visual gerendert statt fälschlich im Window-RenderTarget erwartet.

127 deklarierte Core-Testfälle. Die statische Quellprüfung deckt zusätzlich explizite v0.3-Feature-Anker ab; 16 Python-Tooling-Tests laufen fehlerfrei. Ein randomized Fuzz-Check des Diff-Backtracking wurde in der Erstellungsumgebung zusätzlich gegen die Rekonstruktion beider Eingabesequenzen ausgeführt. C#-Kompilierung, WPF, DPAPI, Clipboard, SendInput und OCR wurden hier mangels .NET-SDK/Windows weiterhin nicht ausgeführt.

## 0.2.0 · 7. Oktober 2026

### Daten und Wiederherstellung

Die letzte manuelle Löschung lässt sich zehn Minuten lang rückgängig machen. Der Undo-Datensatz liegt mit dem Live-Verlauf in einem gemeinsamen verschlüsselten Index. Er übersteht einen Neustart innerhalb der Frist. Die ursprünglichen IDs, Formate, Zeitstempel, Pins, Favoriten und Tags bleiben beim normalen Wiederherstellen erhalten. Zwischenzeitlich erneut kopierte Duplikate werden zusammengeführt, statt doppelt angezeigt zu werden.

Undo hält maximal 128 MiB logischen Inhalts zusätzlich zum Live-Verlauf. Die nächste manuelle Löschung ersetzt die vorherige Löschkopie. Eine ausdrücklich permanente Löschung verwirft auch die vorherige Undo-Kopie. Ein eigener Verwerfen-Button entfernt nur die Löschkopie. Undo wird nicht in normale Backups exportiert und taucht nicht in Suchergebnissen auf. Die Wartung prüft den Ablauf jede Minute sowie beim Start.

Index-Version 1 wird gelesen und erst bei einem erfolgreichen Schreibvorgang in Version 2 überführt. Ein mit Version 2 geschriebener Index darf nicht mit Version 0.1.0 geöffnet werden. Die Schritte für Update und Rückkehr zu einem vollständigen alten Datenstand stehen in `docs/MIGRATION.md`.

### Behobene Fehler und begrenzter Speicherbedarf

Backup-, Text- und PNG-Exporte schreiben zunächst eine vollständige temporäre Datei im Zielverzeichnis. Erst danach wird das Ziel atomar ersetzt. Eine abgebrochene oder gescheiterte Erstellung soll eine vorhandene Zieldatei nicht mehr vorzeitig abschneiden.

Backup-Importe übernehmen nun ursprüngliche Erstellungs-, Kopier- und Einfügezeitpunkte sowie den Kopierzähler. Ungültige Metadaten werden abgelehnt. Speicherdateien werden vor dem Commit gegen dieselben Dateigrößenlimits geprüft, die auch beim späteren Lesen gelten. Doppelte Blob-IDs im Index werden nicht mehr akzeptiert.

Mehrfachkopieren lädt Inhalte nacheinander, zählt UTF-8-Bytes einschließlich Trennzeichen und bricht ab, bevor ein zu großes Ergebnis zusammengefügt wird. Nicht benötigte Rich-/Bild-Payloads werden nicht gesammelt. Mitgliedschaftsprüfungen großer Auswahlen verwenden HashSets statt wiederholter linearer Suchen.

### Windows-Workflow

`build.cmd` kann ein fehlendes stabiles .NET-10-SDK in `.tools` einrichten. Es lädt Microsofts Release-Metadaten und ZIP, kontrolliert Ursprung/Pfad und SHA-512 und führt kein heruntergeladenes Installationsskript aus. Kein systemweiter Installer und keine Administratorrechte werden dafür angefordert. Die SDK-Untergrenze ist 10.0.401.

Build-Verzeichnisse, ZIPs und Berichte erhalten eine eigene Build-ID. Fehlgeschlagene neue Builds überschreiben kein vorheriges erfolgreiches Paket. Die Projektversion stammt aus `Directory.Build.props`, nicht aus einer zweiten hart codierten Versionsnummer im Build-Skript.

`--ui-smoke` erstellt den echten WPF-Workspace mit synthetischen Daten, prüft Bindings und rendert Light/Dark-Workspace, Einstellungs-Drawer, Undo-Leiste und Inline-Editor. Er startet weder Clipboard-Capture noch den echten Datenordner. Der Build ruft die UI- und nativen Smoke-Checks auf passender Architektur auf und bricht deren Prozess nach einem Timeout ab. Manuelle visuelle Abnahme bleibt ein eigener Schritt.

Der CI-Workflow enthält jetzt einen Linux-Core-Testjob und Windows-Builds für x64/ARM64. Berichte und tatsächliche UI-Smoke-Screenshots werden als Workflow-Artefakte gesammelt. Der Workflow wurde nicht von dieser Erstellungsumgebung aus gestartet.

### Prüfstand

110 deklarierte Core-Testfälle, davon 33 in dieser Version hinzugefügt. XML-, Ressourcen-, C#-Delimiter- und ausgewählte Quelltext-Invarianten werden durch den wiederholbaren Python-Validator geprüft. **C#-Kompilierung, Core-Testausführung und Windows-UI-/Native-Smoke wurden hier nicht ausgeführt.** Details: `docs/VALIDATION.md`.

## 0.1.0

Erste Quellversion des nativen Windows-Clipboard-Workspace mit getrenntem Core, WPF-Oberfläche, DPAPI-Speicher, Suche, Tags, Vorschau, Bearbeitung, Textwerkzeugen, lokaler OCR, Paste-Queue und verschlüsselten Backups.
