# Architektur

## Windows statt Browser-Hülle

Die Oberfläche ist WPF mit C# und .NET 10. Die eigentliche Clipboard-Arbeit erfolgt über native Windows-Schnittstellen. Es läuft kein Webserver, Electron-Renderer oder WebView mit privilegierter JavaScript-Bridge. Als Nachteil ist die Oberfläche nicht plattformübergreifend. Diese Entscheidung ist hier gewollt.

## Drei voneinander getrennte Arbeitsbereiche

Der WPF-Dispatcher besitzt UI, Auswahl, Einstellungen und Benutzeraktionen. Ein eigener STA-Thread mit explizitem DispatcherSynchronizationContext besitzt sämtliche OLE-Clipboard-Aufrufe und sein unsichtbares Message-Only-Fenster. Ein Worker verarbeitet maximal vier wartende Capture-Payloads und persistiert sie. Die Queue ist begrenzt; Überlast wird angezeigt, statt unbegrenzt Arbeitsspeicher zu belegen.

`AddClipboardFormatListener` liefert Clipboard-Änderungen. Es gibt keinen Clipboard-Polling-Timer. Vor und nach dem Lesen wird die Clipboard-Sequenz verglichen. Vorübergehend belegte Zwischenablagen werden mit begrenztem Backoff erneut angefragt. Eine fremde Anwendung kann ihren Clipboard-Provider trotzdem blockieren; die separate STA schützt dann die UI, nicht die Verfügbarkeit dieses Providers.

Jeder Capture erhält eine Epochennummer. Pause, Session-Lock und geänderte Ausschlüsse erhöhen diese Nummer. Veraltete wartende Captures werden verworfen. Vor der Speicherung prüft der Worker die aktuelle Datenschutzentscheidung erneut. Eine bereits laufende atomare Speicherung kann beim Sperren noch abschließen; die Funktion verspricht kein nachträgliches Zurückrollen bereits begonnener Transaktionen.

## Inhalt ist nicht Vorschau

`ClipPayload` enthält die vollständigen unterstützten Formate. `ClipEntry` enthält ausschließlich die Metadaten und begrenzte Such-/Vorschaustrings. Kopieren, Bearbeiten, Exportieren und Paste-Queue laden Original-Payloads über das Repository. Kein Paste-Pfad verwendet `PreviewText`.

PNG wird im Speicher nicht erneut kodiert, wenn der Clipboard-Provider bereits PNG anbietet. Für die Anzeige wird eine kleinere Bitmap dekodiert und als Frozen Freezable an WPF übergeben. Original-Payload und Thumbnail sind getrennte Objekte. OCR verändert ausschließlich das abgeleitete Textfeld.

## Speichertransaktionen

Eine benutzerspezifische Single-Instance-Signalisierung wird durch einen exklusiven Dateilease für den Datenordner ergänzt. Dadurch können auch unterschiedliche Logon-Sessions nicht parallel denselben Index schreiben. Dateinamen sind zufällige GUIDs. Index, Inhalt und Einstellungen werden vor dem Schreiben mit Current-User-DPAPI geschützt. Der Index enthält keine unverschlüsselten Tags, Titel oder Suchtexte.

Bei einem neuen oder bearbeiteten Inhalt entsteht zuerst ein neuer Blob. Beide Dateien werden über temporäre Dateien im gleichen Verzeichnis, Flush und Rename geschrieben. Erst wenn der neue Index committed ist, ändert sich der In-Memory-Snapshot. Alte Blobs werden anschließend bestmöglich entfernt. Beobachterfehler können einen bereits abgeschlossenen Commit nicht als vermeintlich fehlgeschlagen zurückrollen.

Nach einem Prozessabbruch kann ein verwaister Blob existieren. Beim Start werden zuerst Indexschema und referenzierte Dateiexistenz geprüft. Nur danach werden unreferenzierte Blobs gelöscht. Ein fehlender oder nicht entschlüsselbarer Index bei vorhandenen Inhalten führt zum Abbruch ohne automatische Bereinigung. Es wird keine frische leere Historie über beschädigte Daten geschrieben.

Das ist crash-resistentes Speichern, keine Garantie gegen Hardwaredefekte, Dateisystemfehler oder einen Stromausfall während eines Rename. Es gibt bewusst keine Klartext-Backups und keine alten Indexkopien, durch die gelöschte Inhalte wieder erscheinen könnten.

## Suche

Die häufige Suche ist ein in-memory Scan über begrenzte Metadaten. Die Roh-Payloads bleiben auf Platte, bis sie angezeigt, verwendet oder explizit tief durchsucht werden. Das vermeidet einen zusätzlichen unverschlüsselten FTS-Suchindex. Dafür ist die Deep-Suche linear und mit großen Bild-/Texthistorien teurer. Sie ist abbrechbar; schneller getippte Suchanfragen ersetzen alte Ergebnisse.

Pins stehen vor normalen Einträgen; bei Suchbegriffen werden passende Titel höher gewichtet, anschließend die Aktualität. Es handelt sich um tokenbasierte Teilstring-Suche, nicht um semantische AI- oder Fuzzy-Suche.

## Paste ohne Überraschungen

Der zuletzt aktive Fremdprozess wird über einen Foreground-WinEvent-Hook erfasst. Gespeichert werden HWND, PID und Prozessstartzeit; dadurch reicht ein wiederverwendetes Fensterhandle nicht als Identität aus. Es werden keine Fenstertitel gespeichert.

Eine Paste-Aktion schreibt zuerst die Clipboard-Formate, blendet PasteDock aus und versucht, das Ziel zu aktivieren. Vor `SendInput` werden Session-Lock, Ablaufzeit, Fensteridentität, tatsächlicher Fokus, physisch gehaltene Modifier und Clipboard-Sequenz erneut geprüft. Wird eine Prüfung nicht erfüllt, erfolgt kein blindes Einfügen. Windows kann Eingaben in höher privilegierte Prozesse ablehnen. PasteDock erhöht deshalb nicht automatisch seine Rechte.

Ein erfolgreicher `SendInput`-Rückgabewert bestätigt die Übergabe der Eingaben an Windows. Er beweist nicht, dass ein beliebiges Zielprogramm den Inhalt wirklich übernommen hat. Fokuswechsel und Clipboard-Änderungen lassen sich zwischen Prüfung und fremder Verarbeitung nicht atomar ausschließen.

## Erweiterungspunkte

Neue Formate gehören in ein explizites, begrenztes Payload-Schema mit Validierung. Neue Persistenzimplementierungen müssen die Commit-Reihenfolge und die Trennung von Metadaten und Inhalt erhalten. Ein späterer Updater benötigt vor Aktivierung einen eigenen signierten Release-Kanal. Automatisches Ausführen von Clipboard-Befehlen gehört nicht in dieses Produkt.

## Index-Transaktion in 0.2.0

`HistoryState` enthält Version, Live-Einträge und optional einen `DeletedBatch`. Der gesamte Zustand wird als ein verschlüsseltes Dokument atomar gespeichert und erst danach per `Volatile.Write` für Beobachter veröffentlicht. `InitializeAsync` akzeptiert Version 1 und 2; Orphan-Cleanup berücksichtigt auch die Blob-Referenzen des Undo-Batches. Abgelaufene Batches verschwinden erst nach einem erfolgreichen Index-Commit.

Eine Löschung mit Undo verschiebt nur Referenzen im Index. Wiederherstellung erhält IDs und Blob-Referenzen oder vereinigt bereits neu erfasste Duplikate mit den alten Benutzerannotationen. Restore überschreitet nicht still die Live-Quota und verdrängt keine fremden Clips. Kapazitätsfehler lassen den Undo-Batch unverändert. Die TTL wird über `TimeProvider` injiziert, sodass Grenz- und Ablaufpfade ohne reale Wartezeiten getestet werden können.

`ExportService` verwendet dieselbe atomare Zielersetzung wie interne Speichervorgänge. `PayloadComposer.CombineAsync` lädt selektierte Payloads seriell, behält nur benötigte Bestandteile und prüft die resultierende UTF-8-Größe vor `string.Join`. Der UI-spezifische Undo-Code liegt in einem eigenen Partial des Shell-ViewModels.

`UiSmokeDiagnostics` verwendet die echten WPF-Views, aber einen separaten temporären Datenordner und keinen gestarteten Clipboard-Service. Die Bilder sind RenderTargetBitmap-Ausgaben des tatsächlichen Visual Trees. Eine Automatisierung, die ein Bild schreiben kann, hat noch keine visuelle Abnahme bestanden; `uiSmokeChecksPassed` und `uiAcceptanceTested` sind im Build-Manifest getrennt.

## Interaktive Produktpfade in 0.3.0

Der 2-Clip-Diff liegt im plattformneutralen Core. Die UI lädt exakt die zwei ausgewählten vollständigen Payloads und reicht ihre Plain-Text-Repräsentationen an `TextDiff`. Gemeinsame Präfix-/Suffixzeilen werden zuerst abgeschnitten. Der mittlere Bereich nutzt einen auf 512 Edits begrenzten Myers-Diff; oberhalb der Grenze wird der Mittelblock deterministisch als entfernt plus hinzugefügt dargestellt. So bleibt die Obergrenze der Trace-Allokation unabhängig von beliebig großen Änderungsdistanzen.

`Alt+1...9` ist kein zweiter Paste-Stack. Der Shortcut löst lediglich den sichtbaren gefilterten Eintrag auf und führt anschließend denselben `PasteService`-Pfad wie normales Einfügen aus. Ziel-App-Plain-Text-Regeln werden unmittelbar vor dem Clipboard-Schreiben ausgewertet. Damit bleiben Fokusprüfung, Prozessidentität, UIPI-Verhalten und Clipboard-Sequence-Schutz an einer Stelle.

`Paste as file` materialisiert genau eine vorhandene Payload über `TemporaryPasteFileService`. Das Schreiben delegiert an `ExportService`, damit auch temporäre Dateien zuerst vollständig gestaged werden. Anschließend wird ein gewöhnlicher File-Drop-Payload an den bestehenden Paste-Pfad übergeben. Die Klartextdatei gehört ausdrücklich nicht zum DPAPI-History-Speicher und wird nicht in den History-Index aufgenommen.

Quell-App-Aktionen verändern keine Capture-Policy heimlich durch das Löschen eines Eintrags. `Exclude source` schreibt eine normalisierte Prozessregel in die bestehenden Einstellungen. `Delete history from source` bildet dagegen eine konkrete ID-Menge aus dem aktuellen Snapshot und durchläuft die bereits vorhandene Löschbestätigung mit verschlüsseltem Undo.
