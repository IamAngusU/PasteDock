# PasteDock

Ein lokaler Clipboard-Workspace für Windows. Wiederfinden, bearbeiten und weiterverwenden, ohne die eigentlichen Inhalte auf eine gekürzte Vorschau zu reduzieren.

**Historische Funktionsreferenz für 0.3.0.** Aktueller Einstieg: [README](../README.de.md). Aktuelle Testergebnisse: [Release-Validierung](RELEASE-VALIDATION.md).

## Neu in 0.3.0

Der Ausbau beginnt mit einem dokumentierten Feature-Diff statt mit einer zweiten Implementierung bestehender Funktionen. Neu sind ein nativer 2-Clip-Diff mit begrenzter Laufzeit, `Alt+1...9` für direktes Einfügen sichtbarer Treffer, Paste-as-File mit konservativer Code-Endungserkennung, stärkere Suchoperatoren, kontextuelle Datenschutzaktionen pro Quell-App und persistente Plain-Text-Regeln pro Ziel-App. Der Diff-Check und Konkurrenzvergleich stehen in [PRODUCT-AUDIT.md](PRODUCT-AUDIT.md) und [V030-DIFF-AUDIT.md](V030-DIFF-AUDIT.md).

127 Core-Testfälle sind deklariert. Die statische Quellprüfung und insgesamt 16 Python-Tooling-Tests laufen in dieser Erstellungsumgebung; C#-Kompilierung und Windows-Laufzeitabnahme bleiben mangels .NET-SDK/Windows offen.

## Oberfläche und Workflow

Eine zusammenhängende Lightmode-Oberfläche mit Navigation, Verlauf und vergrößerbarer Vorschau. Die Eintragsliste ist virtualisiert; ein Grid-Splitter verändert das Verhältnis von Liste und Vorschau. Tags, Textwerkzeuge, Befehle und Löschbestätigung öffnen kontextbezogene Popovers. Einstellungen erscheinen in einem vergrößerbaren Sidedrawer ohne Blur-Backdrop. Darkmode und reduzierte Bewegung sind enthalten. Schriftarten stammen aus Windows, es werden keine Webfonts geladen.

Mehrfachauswahl verwendet das übliche Ctrl-/Shift-Verhalten. Textauswahl lässt sich gemeinsam einfügen; Dateiauswahl wird zu einer File-Drop-Liste zusammengefasst. Unterschiedliche Formate lassen sich nacheinander über die explizite Paste-Queue verwenden. Kein blindes automatisches Weiterlaufen durch eine komplette Queue.

## Implementierte Funktionen

| Bereich | Umsetzung |
|---|---|
| Inhalte | Unicode-Text, HTML/RTF als Clipboard-Formate, PNG und Windows-Bitmap-Fallback, Dateireferenzen |
| Wiederfinden | Sofortsuche, vollständige Deep-Suche, `#tag`, `app:`, `type:`, `is:pinned`, `is:favorite`, `is:protected` (pin/favorite, also vor Retention geschützt), `has:ocr`, `copies:N`, `after:`, `before:` und Anführungszeichen für Phrasen |
| Organisation | Pins, Favoriten, Tags, deduplizierte Historie mit Kopierzähler, Inline-Bearbeitung und eigene Snippets |
| Wiederverwenden | Originalformat, Plain Text, Mehrfach-Paste, FIFO-Queue, `Alt+1...9`, temporäre Datei-Pastes und Plain-Text-Zielregeln pro App |
| Textwerkzeuge | JSON formatieren/minimieren, Zeilen trimmen/sortieren/deduplizieren, LF, Groß-/Kleinschreibung, URL/Base64 und begrenzter 2-Clip-Line-Diff |
| Bilder | Zoom, Pan, Fit, Export des Original-PNG; lokale OCR auf ausdrückliche Aktion |
| Datenschutz | DPAPI, App-Ausschlüsse, direkte Quelle-ausschließen/Quelle-löschen-Aktionen, private Clipboard-Marker, bestmögliche Secret-Erkennung, Pause und Session-Lock |
| Speicherung | Atomare Dateiersetzung, neue Blob-Version pro Bearbeitung, Quoten, geschützte Pins/Favoriten und verschlüsseltes Lösch-Undo |
| Backup | Separates AES-256-GCM-Archiv mit passwortabgeleitetem Schlüssel; Import ohne Verdrängung vorhandener Inhalte |
| Windows | Tray, Single Instance, neu registrierbarer Hotkey, optionaler benutzerspezifischer Autostart, keine Admin-Anforderung |

Die UI ist Englisch, die Einrichtungsdokumentation Deutsch. Es gibt keine Cloud-Synchronisierung, Konten, Telemetrie, Werbeflächen oder Remote-AI-Aufrufe.

## Größen und Grenzen

Standardmäßig 2.000 Einträge, 30 Tage für ungeschützte Historie, 512 MiB Inhaltsbudget. Einstellbar bis 10.000 Einträge und 4 GiB Inhaltsbudget. Einzelne Inhalte sind auf 64 MiB und Bilder zusätzlich auf 40 Millionen Pixel begrenzt. Zu große Kopien werden übersprungen, nicht still gekürzt.

Die Sofortsuche verwendet einen verschlüsselten Index mit bis zu 4.096 Zeichen pro Eintrag. `Deep` durchsucht die vollständigen gespeicherten Inhalte und ist entsprechend teurer. Die Textvorschau zeigt zunächst bis zu 65.536 Zeichen und kann erweitert werden. Kopieren, Bearbeiten und Exportieren verwenden die vollständige Payload, niemals den Vorschaustring.

Vorhandene PNG-Daten werden bytegenau gespeichert. Bei ausschließlich vorhandenem Windows-Bitmap wird daraus ein PNG erzeugt. Nicht jedes Quellprogramm stellt ursprüngliche Dateimetadaten, Farbräume oder Alphakanäle gleich bereit. Die Bildschirmvorschau wird zur Speicherbegrenzung auf maximal 2.560 Pixel Kantenlänge dekodiert. Ihr 100%-Zoom ist daher Vorschau-Pixelgröße, nicht zwingend Originalpixelgröße. Das exportierte PNG wird davon nicht verändert.

OCR verwendet Windows und vorhandene lokale Erkennungssprachen. Es wird kein Modell oder Sprachpaket heimlich heruntergeladen. OCR verarbeitet bei Bedarf eine skalierte Arbeitskopie. Die Original-PNG-Daten bleiben unverändert. HTML wird nicht in einem Browser ausgeführt; HTML/RTF werden als Clipboard-Formate bewahrt, die Anzeige ist Plain Text.

Dateien sind **Referenzen**, keine Sicherung ihrer Dateiinhalte. Verschobene oder gelöschte Quelldateien lassen sich aus dem Verlauf nicht wiederherstellen. Ein ursprünglicher Cut/Move-Vorgang wird nicht gespeichert; PasteDock stellt Dateien als Kopierauswahl bereit. Virtuelle Outlook-Anhänge, beliebige OLE-Objekte, Audio, Video und benutzerdefinierte Formate werden nicht persistiert.

Das Inhaltsbudget zählt die Rohinhalte, nicht den gesamten Speicherverbrauch. Verschlüsselung, serialisierte Metadaten und Dateisystem-Overhead kommen hinzu. Backups sind auf 128 MiB Rohinhalte begrenzt und werden im Speicher zusammengestellt. Sehr große Historien sind nicht durch ein unbegrenztes Streaming-Backup abgedeckt.

## Tastatur

| Shortcut | Aktion |
|---|---|
| `Ctrl+Alt+V` | Anzeigen/ausblenden, konfigurierbar |
| `Ctrl+Alt+N` | Nächstes Element der Paste-Queue |
| `Enter` / `Shift+Enter` | Einfügen / Plain Text |
| `Alt+1...9` | Einen der ersten neun sichtbaren Treffer direkt einfügen |
| `Ctrl+D` | Genau zwei ausgewählte textfähige Clips vergleichen |
| `Ctrl+C` | Ausgewählte Clips kopieren; in Textfeldern die markierte Textauswahl |
| `Ctrl+P` / `Ctrl+B` | Pin / Favorit |
| `Ctrl+E` / `Ctrl+N` | Bearbeiten / neues Snippet |
| `Ctrl+T` / `Ctrl+Q` | Tags / zur Queue hinzufügen |
| `Delete` | Löschung im Popover bestätigen |
| `Ctrl+Z` | Letzte Löschung rückgängig machen, außerhalb von Textfeldern |
| `Ctrl+F` / `Ctrl+K` | Suche / Befehlsmenü |
| `Ctrl+,` | Einstellungen |
| `Ctrl+Enter` | Inline-Bearbeitung speichern |
| `Escape` | Popover schließen, Bearbeitung abbrechen oder ausblenden |

Während einer Texteingabe behalten normale Editor-Shortcuts ihre Funktion. `Ctrl+A` in der Suche markiert den Suchtext; in der Verlaufsliste markiert es die Einträge.

## Aufbau

```text
src/
  PasteDock.Core/             Modelle, Suche, Transformationen, Transaktionen, Backup
  PasteDock.Desktop/
    Clipboard/               Eigener STA-Dispatcher, Clipboard-Listener und Paste
    Interop/                 DPAPI, Hotkeys, Fensteridentität, Win32-Strukturen
    Services/                Workspace, OCR, Tray, Einstellungen, Diagnostik
    ViewModels/              Befehle, Auswahl, Vorschau und Bearbeitungszustände
    Views/                   Hauptfenster
    Controls/                Bild-Canvas
    Themes/                  Light/Dark-Tokens und wiederverwendbare Controls
tests/PasteDock.Core.Tests/   Dependency-freier C#-Testrunner
scripts/                     Build, Test, Paketierung
.github/workflows/           Linux-Core-Prüfung und Windows-CI für x64/ARM64
```

Die Runtime benötigt keine Drittanbieter-NuGet-Bibliotheken. Microsofts Windows-/WinRT-/Desktop-Referenzpakete kommen über das SDK. `Core` kennt keine WPF-, Tray- oder Clipboard-API und kann separat getestet werden. Weitere Architekturdetails: [ARCHITECTURE.md](ARCHITECTURE.md).

## Bauen

```powershell
.\build.cmd
# oder
.\scripts\build.ps1 -Runtime win-arm64 -Bootstrap
# kleinere Distribution mit separat benötigter Desktop-Runtime
.\scripts\build.ps1 -FrameworkDependent
```

Kein Trimming, kein experimenteller Native-AOT-Publish, kein selbstgebauter Updater mit ungeprüften Downloads. Der Build veröffentlicht einen selbständigen Ordner plus ZIP und Hash. Für öffentliche Releases fehlen noch Windows-Abnahme und eigenes Code-Signing. Ein SHA-256-Hash ist keine Herausgebersignatur.

Tests und Datenschutzmodell: [VALIDATION.md](VALIDATION.md), [SECURITY.md](SECURITY.md). Produkt-/Konkurrenz-Diff: [PRODUCT-AUDIT.md](PRODUCT-AUDIT.md).

## SDK und Prüfbilder

`build.cmd` verwendet ein passendes vorhandenes SDK oder richtet ein geprüftes SDK lokal im Projekt unter `.tools` ein. Neue Build-Ordner, ZIP-Dateien und Berichte haben jeweils eine eindeutige Build-ID. Die App selbst bleibt offline. UI-Smoke-Renderings entstehen erst bei einem ausgeführten Windows-Build und liegen dann neben den JSON-Prüfberichten unter `artifacts/reports/<Build-ID>/`. Sie sind echte WPF-Renderings mit synthetischen Daten, kein Ersatz für eine manuelle UI-Abnahme.

Ein manuell gelöschter Batch kann bis zu 128 MiB zusätzliche logische Inhaltsdaten für Undo halten. Die Oberfläche weist diese Daten separat aus. Ein abgelaufener Lösch-Undo-Batch wird bei der nächsten minütlichen Wartung oder beim App-Start bereinigt. Temporäre Paste-Dateien werden 24 Stunden nach ihrem eigenen UTC-Erzeugungszeitpunkt cleanup-fähig und ebenfalls nur bei laufender App beziehungsweise einem späteren Start entfernt. Backups enthalten nur den Live-Verlauf.
