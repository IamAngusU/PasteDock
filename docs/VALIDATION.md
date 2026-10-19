# Validierungsstand

Stand dieses Quellpakets: PasteDock 0.3.0, 7. Oktober 2026.

## Tatsächlich durchgeführt

In der Erstellungsumgebung stehen Linux, Python und XML-/Quelltextanalyse zur Verfügung. Dort werden XML-/XAML-/Projektdateien, Ressourcenverweise, deklarierte Eventhandler, Dateireferenzen, C#-Tokenbalance und projektbezogene Sicherheitsinvarianten statisch geprüft. Der maschinenlesbare Bericht liegt unter `docs/static-validation.json`. Zusätzlich wurden **16 Python-Tooling-Tests** ausgeführt: zwölf für den Quellprüfer und vier für den Feature-Audit. Sie prüfen unter anderem Literal-/Kommentarerkennung, die ehrliche Kennzeichnung des statischen Berichts, vollständige Feature-Alias-Abdeckung und typische Vorhandenheitsabfragen. Sie sind keine Anwendungstests.

Der Versuch, das .NET-SDK in dieser Umgebung nachzuladen, scheiterte am Netzwerk-/DNS-Zugriff. Es stehen weder ein .NET-SDK noch Windows oder PowerShell zur Verfügung. SDK-Bootstrap und PowerShell-Buildskripte sind deshalb ebenfalls nicht ausgeführt.

**Nicht durchgeführt:** C#-Kompilierung, Ausführung des Core-Testrunners, WPF-Start, Windows-DPAPI, Clipboard-/SendInput-Integration, OCR und die visuelle Abnahme auf Windows. Das Erstellen und Lesen dieser Implementierung ist kein Ersatz für diese Laufzeittests. Die Build-Skripte, Testfälle und der CI-Workflow sind vorhanden, aber ein erfolgreiches Windows-Build-Ergebnis wird mit diesem Quellpaket nicht behauptet.

Die Strukturprüfung ist wiederholbar und benötigt nur Python 3.10 oder neuer:

```powershell
python scripts/validate-source.py
python scripts/audit-features.py
python -m unittest discover -s scripts/tests -v
```

Der zusätzliche Feature-Audit prüft 14 bewusst gewählte Source-Anker und schreibt `docs/feature-presence.json`. Im hier erzeugten Stand wurden **14/14 Anker gefunden**. Das verhindert versehentliche Doppelplanung und tote Dokumentation, beweist aber weder C#-Kompilierbarkeit noch korrektes Laufzeitverhalten.

Sie ist bewusst kein C#-Compiler. Insbesondere beweisen balancierte Zeichen, vorhandene Eventhandler und gefundene Ressourcennamen weder korrekte Typen noch funktionierende WPF-Bindings.

## Automatisierte Tests nach dem Entpacken

```powershell
.\scripts\test.ps1 -Bootstrap
# Nur die plattformunabhängigen Core-Tests:
dotnet run --project tests/PasteDock.Core.Tests -- --json artifacts/reports/core-tests.json
```

Der dependency-freie C#-Testrunner enthält **127 deklarierte Testfälle**: 110 aus dem 0.2.0-Stand plus **17 neue Regressionen für 0.3.0**. Keiner dieser C#-Tests wurde in der Erstellungsumgebung ausgeführt. Er prüft Klassifizierung, Tags, Unicode, Suchfilter, Quoten, Pins/Favoriten, Transformationen, vollständige Inhalte, Duplikate, konkurrierende Captures, Abbruch, atomare Fehlerpfade, beschädigte Speicherdateien, Deep-Suche und authentifizierte Backups. Storage-Tests benutzen einen zufälligen Testschlüssel und einen eigenen Test-Protector. Das ist ausdrücklich kein Ersatz für native DPAPI-Tests.

Der Windows-Diagnostikmodus prüft INPUT-Strukturgrößen, DPAPI-Roundtrip und Manipulationsablehnung, die Clipboard-Sequence-API und Registrierung eines STA-Clipboard-Listeners. Er liest keine Clipboard-Payloads, verändert die Zwischenablage nicht und injiziert keine Tasten. OCR-Sprachverfügbarkeit ist eine Information, kein Muss für einen erfolgreichen Start.

```powershell
# Bereits veröffentlichte App, auf einem Windows-Rechner:
.\PasteDock.exe --diagnostics --report .\native-report.json
```

## WPF-Smoke-Modus und reproduzierbare Berichte

```powershell
.\PasteDock.exe --ui-smoke --report .\ui-report.json
```

Dieser Modus konstruiert die tatsächliche WPF-Oberfläche gegen einen zufällig benannten temporären Test-Workspace. Er startet keinen Live-Clipboard-Monitor, öffnet nicht die vorhandene Benutzerhistorie und injiziert keine Eingaben. Er prüft eine gebundene Verlaufsliste, protokolliert Binding-Fehler und erzeugt sieben echte RenderTargetBitmap-Bilder: Workspace und Einstellungen in Light/Dark sowie 2-Clip-Diff, Lösch-Undo und Inline-Editor in Lightmode. Fehler beim Aufbau, Binding oder Rendern führen zu einem Fehler-Exitcode. Die Bilder entstehen erst durch Ausführen unter Windows; dieses Quellpaket enthält keine vorgetäuschten UI-Screenshots.

`build.cmd` führt Core-Tests, Publish und bei passender Hostarchitektur beide Smoke-Modi aus. Exitcode und Status der Smoke-Berichte werden geprüft. Ein Smoke-Prozess erhält höchstens zwei Minuten. Fehler verhindern die erfolgreiche Paketierung. ARM64-Crossbuilds auf einem x64-Host sind davon ausdrücklich kein nativer Laufzeitnachweis.

Jeder Build erhält eine UTC-/Zufallskennung. Die Berichte liegen unter `artifacts/reports/<Build-ID>/`, zusätzlich als `validation-reports/` im jeweiligen fertigen Paket. Das Build-Manifest trennt ausgeführte Core-Tests, bestandene native Checks, bestandene UI-Smoke-Checks und **nicht** durchgeführte manuelle UI-Abnahme. `-SkipTests` setzt diese Testnachweise nicht auf erfolgreich. Der Zeiger `artifacts/last-successful-build.json` wird nur nach erfolgreicher Paketierung aktualisiert; bei einem Fehler kann er weiterhin auf einen älteren Build zeigen.

Ein erfolgreiches Rendern beweist nicht, dass jede Oberfläche gut aussieht oder alle Interaktionen funktionieren. DPI, Tastatur, Fokuswechsel, Barrierefreiheit, Popoverpositionen, mehrere Monitore und reale Clipboard-Provider benötigen weiterhin die folgenden Tests.

## Regressionen in 0.2.0 und 0.3.0

Die 0.2.0-Fälle umfassen persistentes Undo einschließlich Neustart, exakt zehn Minuten Ablaufzeit per injizierter Uhr, ersetzte Löschkopien, fehlgeschlagene und abgebrochene Commits, Quotenfehler ohne Verdrängung, permanente Löschung, Metadaten-Merge bei neu kopierten Duplikaten, verschlüsselten Undo-Inhalt, Formatmigration, Indexvalidierung, historische Importzeitstempel, atomare Exporte und Größen-/Abbruchgrenzen beim Zusammenfügen.

Die **17 neuen 0.3.0-Fälle** decken Normalisierung der Plain-Text-Zielregeln, Parser und Matcher für `is:protected`, `has:ocr`, `copies:N` und `before:`, konservative Entwickler-Dateiendungen sowie den begrenzten Text-Diff ab. Beim Diff werden identische Texte, Ersetzung, Einfügung, leere Seiten, CRLF/LF, Whitespace-Ignorieren, Unified-Diff-Labels, Fallback und Zeichen-/Zeilengrenzen geprüft. Diese C#-Fälle sind implementiert, ihre Laufzeitresultate hier aber nicht bekannt.

## Windows-Abnahme vor echter Nutzung mit wichtigen Daten

| Szenario | Erwartung | Ausgeführt |
|---|---|---|
| Start, X, Tray, erneuter Start | Nur eine Instanz; X blendet aus; Tray zeigt wieder an | Nein |
| Ctrl+Alt+V, bestehender Hotkey-Konflikt | Öffnet den Verlauf oder zeigt einen Konflikt ohne Verlust des alten Hotkeys | Nein |
| 150.000+ Zeichen Unicode/Code | Vollständiger Roundtrip, begrenzte Vorschau, Deep-Suche findet den Inhalt am Ende | Nein |
| HTML/RTF aus Word und Browser | Unterstützte Rich-Formate bleiben für Einfügen erhalten; Vorschau führt nichts aus | Nein |
| PNG mit Alpha, großer Screenshot | Original-PNG bleibt erhalten; Vorschau skaliert unabhängig | Nein |
| Explorer-Dateien, verschobene Dateien | File-Drop ohne Move-Semantik; keine Behauptung einer Datei-Sicherung | Nein |
| Mehrfachauswahl, reine Text-/Dateiauswahl | Reihenfolge beziehungsweise Dateiliste korrekt | Nein |
| Gemischte Queue | Ein Shortcut fügt genau einen Eintrag ein; Fehler verbraucht ihn nicht | Nein |
| Fokuswechsel oder fremde Clipboard-Änderung während Paste | Keine blinde Injection; klare Statusmeldung | Nein |
| Administrator-Zielfenster | Manuelles Paste-Fallback statt automatischer Privilegienerhöhung | Nein |
| Passwortmanager und Private-Marker | Überspringen ohne Klartext im Log | Nein |
| Windows sperren während Suche/Paste/Bearbeitung | Capture/Paste blockiert, Queue geleert, Entwurf im Speicher erhalten | Nein |
| Schnelle Kopien und fremde Clipboard-Sperre | Begrenzte Retries/Queue; UI bleibt bedienbar; Überspringen wird angezeigt | Nein |
| Prozessabbruch zwischen Blob und Index | Kein Index verweist auf halbe Daten; validierte Orphan-Bereinigung | Nein |
| Fehlender/defekter Index oder fehlender Blob | Startfehler ohne automatische Löschung weiterer Daten | Nein |
| Voller Datenträger und verweigerter Schreibzugriff | Kein In-Memory-Commit vor durablem Index-Commit | Nein |
| Niedrigere Quota mit vielen Pins/Favoriten | Geschützte Inhalte bleiben; neue Captures können blockiert werden | Nein |
| Falsches Backup-Passwort und manipuliertes Archiv | Import scheitert ohne Teilimport | Nein |
| Löschen, Ctrl+Z, Neustart vor Ablauf | Inhalt und Annotationen wiederherstellbar; Clipboard bleibt unverändert | Nein |
| Zehn Minuten Ablauf, App geschlossen, Neustart | Undo nicht mehr verfügbar; Bereinigung erst beim erneuten Start, kein Hintergrundversprechen | Nein |
| Zweite Löschung, permanente Löschung, Verwerfen | Genau die dokumentierte Löschkopie bleibt beziehungsweise wird entfernt | Nein |
| Quota beim Wiederherstellen ausgeschöpft | Live-Daten und Undo bleiben unverändert; keine automatische Verdrängung | Nein |
| Format-1-Historie laden und schreiben | Lesen möglich; erster erfolgreicher Commit schreibt Format 2 | Nein |
| Export über bestehende Datei, Fehler und Abbruch | Kein absichtliches Vorabkürzen der Zieldatei; Staging-Datei aufgeräumt | Nein |
| Sehr große Mehrfachauswahl | Größenlimit vor der finalen String-Allokation geprüft; spätere Payloads nicht mehr geladen | Nein |
| 2-Clip-Diff, kleine und sehr große Änderung | Quellen unverändert; normaler Line-Diff beziehungsweise begrenzter Fallback ohne UI-Hänger | Nein |
| `Alt+1...9` bei aktiver Suche | Paste entspricht exakt der sichtbaren Trefferreihenfolge und verwendet den normalen Fokus-/Zielschutz | Nein |
| Paste-as-File für Text, Code und PNG | Lesbare temporäre Datei, passende konservative Endung, normaler File-Drop-Paste, spätere Cleanup-Eignung | Nein |
| Paste-as-File mit sensiblem Testinhalt | Datei liegt nur lokal, ist als Temporary/NotContentIndexed markiert; Klartextgrenze ist in UI/Doku erkennbar | Nein |
| Quell-App ausschließen und separat Quell-Historie löschen | Ausschluss betrifft Zukunft; Löschung braucht Bestätigung und nutzt Undo | Nein |
| Plain-Text-Regel pro Ziel-App | Text wird flach eingefügt; Dateien/Bilder ohne OCR behalten Format; Regel über Settings reproduzierbar | Nein |
| Neue Suchoperatoren kombiniert | `is:protected`, `has:ocr`, `copies:N`, `before:` und bestehende Filter bilden eine AND-Abfrage | Nein |
| SDK fehlt, Download-/Checksum-Fehler | Kein ungeprüftes SDK ausgeführt; vorhandene Installation nicht überschrieben | Nein |
| Zwei Builds nacheinander | Eindeutige Ordner, ZIPs und Berichte; kein Austausch alter Dateien | Nein |
| Offline ohne OCR-Sprachpaket | App läuft; OCR meldet fehlende lokale Sprache | Nein |
| 100/125/150/200 % DPI, mehrere Monitore | Lesbare Controls, erreichbare Popup-/Drawer-Inhalte, vernünftige Fensterposition | Nein |
| Darkmode, hohe Kontraste, Tastatur, Screenreader | Fokus sichtbar; alle Kernaktionen bedienbar; zusätzliche Accessibility-Abnahme nötig | Nein |
| Windows ARM64 | Kompilierung und native Funktionen auf echter ARM64-Hardware bestätigen | Nein |

Die CI kann Core-Tests und Windows-Smoke-Checks ausführen und beide Architekturen veröffentlichen. Ein auf x64 cross-kompiliertes ARM64-Paket ist noch kein ARM64-Laufzeitnachweis. Der Workflow veröffentlicht keine GitHub-Releases automatisch.
