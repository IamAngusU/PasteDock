# Aktualisierung auf 0.3.0

## Noch keine Historie vorhanden

Das neue Quellpaket separat entpacken, bauen und die EXE aus dem neu erzeugten Ausgabeordner starten. Kein Server-Upload und keine vorherige Datenmigration sind nötig.

## Von einer bereits genutzten 0.2.0-Installation

0.3.0 erhöht das verschlüsselte History-Indexformat **nicht erneut**. Die in 0.2.0 eingeführte Formatversion 2 bleibt bestehen. Neu ist lediglich die Einstellung `PlainTextPasteApps`; fehlt sie in einer vorhandenen Settings-Datei, startet die Liste leer.

1. PasteDock über das Tray vollständig beenden, nicht nur mit X ausblenden.
2. Vor einem Update mit wichtigen Daten ein passwortgeschütztes `.pdbackup` exportieren. Für einen vollständigen lokalen Rollback zusätzlich den geschlossenen Ordner `%LOCALAPPDATA%\PasteDock` separat sichern.
3. 0.3.0 in einen neuen Programmordner bauen. Nicht einzelne DLLs oder EXEs in einen alten Publish-Ordner kopieren.
4. Die neue EXE aus ihrem vollständigen Publish-Ordner starten. History und Undo-Batch werden ohne erneute Indexmigration geöffnet.
5. Nach dem ersten Start mindestens Suche, normales Paste, `Alt+1`, 2-Clip-Diff und eine temporäre Datei-Paste mit unwichtigem Testinhalt prüfen. Autostart anschließend einmal aus- und wieder einschalten, falls die alte EXE an einem anderen Pfad lag.

Ein Rückstart auf 0.2.0 verändert das History-Indexformat nicht. 0.2.0 kennt jedoch die neue Ziel-App-Regel nicht und kann sie beim nächsten Speichern der Einstellungen verlieren. Zwei Programmversionen nie gleichzeitig gegen denselben Workspace laufen lassen.

## Von einer bereits genutzten 0.1.0-Installation

1. In 0.1.0 ein passwortgeschütztes Backup exportieren und die Passphrase separat sichern. PasteDock danach vollständig beenden.
2. Den gesamten geschlossenen Datenordner `%LOCALAPPDATA%\PasteDock` an einen getrennten lokalen Sicherungsort kopieren. Diese DPAPI-Sicherung benötigt weiterhin dasselbe Windows-Benutzerprofil und ist kein portables Backup.
3. 0.3.0 in einen neuen Ordner entpacken und `build.cmd` ausführen. Keine alten und neuen Binärdateien mischen.
4. 0.3.0 liest Speicherformat 1. Der erste erfolgreiche Schreibvorgang erzeugt Format 2 mit der verschlüsselten Undo-Transaktion. Schon ein automatischer Capture kann dieser Schreibvorgang sein.
5. Danach 0.1.0 **nicht** mehr auf den migrierten Datenordner loslassen. Für einen vollständigen Rollback den gesamten vor dem Update gesicherten Datenordner wiederherstellen.

## Zur vollständigen alten Sicherung zurückkehren

Alle PasteDock-Prozesse schließen. Den aktuellen Datenordner zunächst separat sichern oder umbenennen. Dann den **gesamten** vor dem Update gesicherten Datenordner zurückkopieren und erst danach die alte Programmversion starten. Nicht nur `index.pdv` zurückkopieren: Index und referenzierte Blobs müssen zusammenpassen.

Der separate `.pdbackup`-Export bleibt das portable, formatbewusste Austauschmittel. Aktuelle Importe erhalten die im Archiv gespeicherten Erstell-/Änderungszeitstempel und den Kopierzähler; lokale Quoten und Formatvalidierung gelten weiterhin.

## Neue lokale Dateien in 0.3.0

`Paste as file` erzeugt unter dem PasteDock-Datenstamm einen separaten Ordner `paste-files`. Diese Dateien sind absichtlich **nicht DPAPI-verschlüsselt**, weil das Zielprogramm einen normalen Dateipfad lesen können muss. Sie werden als temporär und nicht für Inhaltsindizierung vorgesehen markiert und nach Ablauf bestmöglich aufgeräumt. Vor dem Kopieren eines kompletten Datenordners auf ein anderes Medium diesen Ordner prüfen oder löschen, falls er sensible Inhalte enthält.

## Ersetzungsübersicht

Das neue Projekt als Ganzes verwenden: `src/`, `tests/`, `scripts/`, `.github/`, `docs/`, `Directory.Build.props`, `global.json`, Startdateien und Dokumentation. Ein partieller Austausch nur der UI oder einzelner Services lässt 0.3.0 unvollständig.

Beim Wechsel einer gebauten Anwendung den vollständigen neuen Ausgabeordner verwenden. `%LOCALAPPDATA%\PasteDock` nicht löschen oder mit Dateien aus dem Quellpaket überschreiben.
