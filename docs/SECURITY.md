# Datenschutz und Sicherheitsgrenzen

## Auf dem Gerät

Index, Titel, Tags, Vorschauen, Suchtexte, Inhalte und Einstellungen werden auf Platte mit Windows-DPAPI im Current-User-Kontext gespeichert. Es wird nicht das rechnerweite Schutzflag verwendet. Das Projekt enthält kein fest eingebautes Verschlüsselungspasswort.

DPAPI schützt kopierte Datendateien nicht gegen alle Angreifer. Prozesse im gleichen Benutzerkontext, ein kompromittiertes Windows-Profil, Administratoren mit geeignetem Zugriff oder Zugriff auf den laufenden Prozess können Inhalte möglicherweise lesen. Im Arbeitsspeicher sind indexierte Metadaten und aktive Inhalte entschlüsselt. .NET-Strings lassen sich nicht verlässlich überschreiben. Die Implementierung löscht temporäre Kryptographie-Bytepuffer, behauptet aber keine vollständige Speicherbereinigung.

Es gibt kein separates Vault-Passwort für die Live-Historie und keine isolierte Sicherheitsgrenze gegenüber anderen Prozessen des angemeldeten Benutzers. Windows-Session-Lock stoppt Capture und Paste und versteckt aktive Vorschauen. Vor Capture und Clipboard-Schreiben wird zusätzlich eine aktive Windows-Session mit zugänglichem Default-Eingabedesktop verlangt. Sichere oder benutzerdefinierte Win32-Desktops sind nicht als Capture-Ziele vorgesehen. Ein ungespeicherter Editorentwurf bleibt im Prozessspeicher, damit Sperren nicht zu Datenverlust führt. Die Paste-Queue wird beim Sperren geleert.

## Verschlüsselte Editorentwürfe

Standardmäßig speichert der Editor Änderungen nach einer Sekunde als separate DPAPI-Datei im Datenordner. Der sichtbare Sicherungsstatus unterscheidet ausstehende, gesicherte und abgelehnte Änderungen. Änderungen vor erfolgreicher Sicherung können bei einem Absturz verloren gehen. Es gibt höchstens einen Entwurf mit 1.048.576 Zeichen und maximal 8 MiB verschlüsselter Dateigröße. Er läuft sieben Tage nach seiner letzten gesicherten Änderung ab; Start, Wiederherstellen und stündliche Wartung prüfen dies.

Die aktuellen Secret-, Ausschluss- und Größeneinstellungen gelten auch für diese Sicherung. Eine abgelehnte oder leere Fassung entfernt die vorherige Rettungskopie. Die Erkennung ist weiterhin heuristisch. Abschalten in den Einstellungen entfernt die Plattenkopie und behält den offenen Text im Arbeitsspeicher. Fehler dabei werden angezeigt. Entwürfe gehören weder zu portablen Backups noch zum normalen Clip-Kontingent. Sie unterliegen denselben DPAPI- und Speicherbereinigungsgrenzen wie die Historie.

## Clipboard ist ein gemeinsam genutzter Betriebssystemdienst

Die App respektiert unterstützte private Clipboard-Marker, schließt konfigurierte Prozessnamen aus und kann einige erkennbare Geheimnisformate überspringen. Standardmäßig sind 1Password, Bitwarden, KeePass, KeePassXC und ProtonPass ausgeschlossen. Ausschlüsse gelten anhand des ermittelten Clipboard-Eigentümers beziehungsweise des aktiven Quellprozesses. Browserbasierte Passwortmanager sind damit nicht automatisch vollständig erkennbar.

Die Secret-Prüfung erkennt beispielweise PEM-Private-Key-Marker, bestimmte GitHub-Tokenformen und AWS-Zugriffsschlüssel-IDs. Sie erkennt weder jedes Passwort noch jeden API-Schlüssel. Einfache Zahlen, Einmalcodes und unauffällige Passwörter können im Verlauf landen. Für sensible Arbeitsphasen Capture vorher pausieren oder den gesamten Quellprozess ausschließen.

Auf Wunsch schreibt PasteDock beim eigenen Kopieren Marker gegen Windows-Clipboard-History und Cloud-Clipboard. Diese Marker sind keine Verschlüsselung der aktiven Zwischenablage und keine Zugriffssteuerung gegenüber anderen Clipboard-Readern. Sie verändern nicht rückwirkend die von einer fremden Anwendung ursprünglich veröffentlichten Inhalte oder bereits vorhandene Win+V-Einträge. Windows-History-/Cloud-Einstellungen bleiben eine eigene Entscheidung des Benutzers.

Die App liest erlaubte Formate explizit. Beliebige .NET/OLE-Objekte werden nicht als persistierte Daten deserialisiert. HTML, URLs, JSON und Code werden nicht ausgeführt. Es gibt keine automatischen Link-Aufrufe, Shell-Kommandos oder Vorschau-Webrequests. Beim tatsächlichen Einfügen gelten natürlich die Regeln und das Verhalten des Zielprogramms.

## Grenzen und Fehlerverhalten

Die Größenprüfung lehnt zu große Einträge ab, statt sie zu kürzen. Clipboard-Provider gehören anderen Programmen und können während der Formatabfrage trotzdem Ressourcen beanspruchen oder blockieren. Die getrennte STA und begrenzte Capture-Queue reduzieren Auswirkungen auf die Oberfläche, machen fremde Provider aber nicht vertrauenswürdig.

PNG-Header werden auf Dimensionen und Pixelbudget geprüft. Für Anzeige/Paste/OCR verwenden wir Windows-Bilddecoder. Headerprüfung ist kein vollständiger Schutz gegen sämtliche Bilddecoder-Schwachstellen. Betriebssystem und Runtime müssen aktuell gehalten werden.

Beschädigte oder nicht entschlüsselbare Speicherdateien führen nicht zum automatischen Neuinitialisieren. Vor Reparaturen den vollständigen Datenordner extern sichern. Die normale Löschaktion entfernt Einträge aus der aktiven Historie, behält aber bis zu zehn Minuten eine verschlüsselte Undo-Kopie. Erst Verwerfen, Ablaufen oder die permanente Löschaktion entfernt die zugehörigen Indexreferenzen und versucht, die Dateien zu löschen. Das ist **kein nachweisbar sicheres Überschreiben von SSDs**, Schattenkopien oder Betriebssystem-Backups. Auch die aktuelle Zwischenablage wird durch Löschen eines Verlaufseintrags nicht automatisch geleert.

## Portable Backups

Ein Backup wird getrennt von DPAPI mit AES-256-GCM geschützt. Der Schlüssel wird mit PBKDF2-HMAC-SHA256, 600.000 Iterationen und einem zufälligen 128-Bit-Salt abgeleitet. Jedes Archiv bekommt eine neue 96-Bit-Nonce. Header, Ciphertext und 128-Bit-Authentifizierungstag werden beim Entschlüsseln geprüft. Der Header trägt die Formatversion; Parameteränderungen benötigen eine neue Formatversion.

Export benötigt mindestens zwölf Zeichen Passphrase. Eine lange einzigartige Passphrase ist wichtiger als das bloße Erreichen dieser Mindestlänge. Es gibt keine Passphrase-Wiederherstellung. Falsches Passwort oder beschädigter Authentifizierungstag verhindern den Import. Ein Import wird gegen die lokalen Größen- und Kapazitätsgrenzen geprüft und verdrängt keine vorhandenen Inhalte. Automatisch erfasste Duplikate werden über formatbewusste Fingerprints erkannt. Bewusst gespeicherte Snippets behalten eine eigene dauerhafte Identität: Identischer Text allein führt sie beim Import nicht zusammen. Ein bereits vorhandenes Snippet derselben Identität wird nicht durch eine ältere Backup-Fassung ersetzt.

Authentifizierte Backup-Inhalte werden beim ausdrücklichen Import nicht erneut durch die heuristischen Capture-Secretfilter entfernt. Ein zuvor bewusst gespeichertes Geheimnis bleibt also Teil einer absichtlich wiederhergestellten Historie. Importierte Formate werden dennoch validiert.

Der Export von einzelnen Text-/PNG-Dateien erzeugt dagegen gewöhnliche, **unverschlüsselte** Dateien. Dieser Export erfolgt nur über einen ausdrücklich ausgewählten Speicherort.

## Temporäre Datei-Pastes und Zielregeln ab 0.3.0

`Paste as file` ist eine bewusste Ausnahme vom verschlüsselten Live-Speicher. Damit das Zielprogramm einen normalen Dateipfad lesen kann, wird eine Klartextdatei unter dem lokalen PasteDock-Bereich `paste-files` erzeugt. Sie wird bestmöglich mit den Windows-Attributen `Temporary` und `NotContentIndexed` markiert. Diese Attribute sind Hinweise an Betriebssystem und Indexer, **keine Zugriffskontrolle und keine Verschlüsselung**. Andere Prozesse im Benutzerkontext können die Datei lesen, solange sie existiert.

PasteDock macht solche Dateien **24 Stunden nach ihrer Materialisierung** löschbar, damit Anwendungen sie nach dem eigentlichen Drop/Paste noch öffnen können. Das Alter stammt aus dem von PasteDock erzeugten UTC-Zeitstempel im Dateinamen und wird nicht durch spätere Inhaltsänderungen der Ziel-App verlängert. Bereinigung erfolgt nur beim App-Start beziehungsweise während laufender Wartung; eine geschlossene Anwendung löscht nichts im Hintergrund, daher kann eine abgelaufene Datei tatsächlich länger auf Platte bleiben. Dateisystem-Journale, Backups, Antivirusprodukte oder SSD-Effekte können weitere Spuren behalten. Für hochsensible Inhalte ist `Paste as file` deshalb nicht der geeignete Pfad.

Die Dateiendung für Code wird ausschließlich lokal und konservativ aus bereits gespeichertem Inhalt abgeleitet. Unklare Inhalte bleiben `.txt`. Kein erzeugter Code wird ausgeführt, kompiliert oder an einen Interpreter übergeben.

Plain-Text-Zielregeln speichern nur normalisierte Prozessnamen. Sie erlauben keine Skripte, Shellbefehle oder regulären Ausdrücke. Prozessnamen dienen hier ausschließlich als Komfortmerkmal für Capture- und Formatierungsregeln und sind **keine vertrauenswürdige Prozessidentität oder Sicherheitsgrenze**; ein anderer Prozess kann denselben Namen verwenden. Vor jedem automatischen Paste gelten weiterhin die bestehenden Ziel-/Fokus-/Session-/Clipboard-Prüfungen. Eine Regel ändert nur die zu schreibenden Clipboard-Formate für textfähige Inhalte; Dateien und normale Bilder werden nicht still in Text umgedeutet.

Der 2-Clip-Diff verarbeitet ausschließlich lokal geladene Payloads. Er verändert die beiden Quellclips nicht. Zeichen-, Zeilen- und Edit-Distance-Grenzen verhindern unbegrenzten Trace-Speicher; bei sehr großen Änderungen wird bewusst ein vereinfachter Blockvergleich verwendet.

Quell-App-Ausschluss und Quell-App-Löschung sind getrennt: Ausschluss betrifft zukünftige Captures. Bereits gespeicherte Einträge bleiben erhalten, bis der Benutzer die separate Löschung bestätigt. Die Quell-App-Löschung verwendet denselben verschlüsselten Undo-Pfad wie andere manuelle Löschungen.

## Logs, Netzwerk und Distribution

Diagnostik protokolliert feste Fehlercodes, Exception-Typen und HResults. Keine Clipboard-Texte, Fenstertitel, Dateinamen oder Exception-Messages werden in das laufende Anwendungslog geschrieben. Native Smoke-Check-Berichte enthalten System-/Runtimeversion und OCR-Sprachcodes, aber keine kopierten Inhalte. Es gibt keine Telemetrie und keinen Netzwerkclient in der App.

Build und Paketwiederherstellung greifen auf Microsoft-/NuGet-Infrastruktur zu. Selbständige .NET-Pakete enthalten Microsoft-Runtime-Komponenten und deren mitgelieferte Lizenz-/Hinweisdateien; diese dürfen bei einer Distribution nicht entfernt werden. Das Projekt lädt keine Fontdateien.

Die erzeugten Binärdateien sind unsigniert. Ein lokaler SHA-256-Hash ist eine Integritätsprüfung, kein unabhängiger Nachweis der Herkunft. Vor öffentlichen Releases: Windows-Abnahme, Review der nativen Grenzen und eigene Authenticode-Signatur.

## Lösch-Undo ab 0.2.0

Die normale Löschaktion behält bewusst eine verschlüsselte Löschkopie für das Rückgängigmachen. Live-Verlauf und Undo-Batch werden gemeinsam in einem DPAPI-geschützten Index committet. Erst nachdem ein neuer Index erfolgreich gespeichert ist, dürfen nicht mehr referenzierte Dateien entfernt werden. Es gibt genau einen Batch, bis zu 128 MiB logischen Inhalts zusätzlich zur Live-Quota. Ein weiterer manueller Löschvorgang ersetzt den Batch.

Die Verfügbarkeit läuft nach zehn Minuten anhand der Windows-UTC-Uhr ab. Der physische Löschversuch erfolgt bei der nächsten minütlichen Wartung, einem expliziten Verwerfen oder beim nächsten Start. Eine beendete App entfernt nichts im Hintergrund. Verstellte Systemzeit kann Fristen beeinflussen. Undo ist weder ein sicheres Überschreiben noch eine Garantie, dass Daten nicht mehr aus Backups, Speicherabbildern oder dem aktuellen Clipboard erhältlich sind.

Für sofortiges Entfernen der App-Dateien ohne Undo die explizite permanente Löschaktion verwenden. Sie verwirft zusätzlich den bisherigen Undo-Batch. „Verwerfen“ in der Undo-Leiste löscht nur die Löschkopie. Abgelaufene beziehungsweise permanent gelöschte Inhalte bleiben nicht absichtlich in einem versteckten Papierkorb. Löschfehler können jedoch verwaiste verschlüsselte Dateien zurücklassen; gültige Indexprüfung geht vor automatischer Bereinigung.

## Build-Bootstrap und UI-Smoke

Der optionale Bootstrap lädt SDK-Metadaten und eine ZIP über HTTPS von Microsoft, beschränkt die Download-URL auf den erwarteten SDK-Pfad und prüft SHA-512 vor dem Entpacken. Prüfsumme und Archiv stammen aus derselben Vertrauensdomäne; das ist keine unabhängige Herkunftsgarantie und keine Authenticode-Signatur der späteren App. Es werden keine entfernten Skripte ausgeführt und kein globales SDK registriert. Dateien liegen unter `.tools`; CLI-Telemetrie wird für den Build-Prozess deaktiviert.

Der WPF-Smoke-Modus nutzt ausschließlich ein zufällig benanntes temporäres Workspace-Verzeichnis mit synthetischen Inhalten. Er startet nicht den Clipboard-Monitor und öffnet nicht die wirkliche Historie. Seine Fehlerberichte dürfen vollständige Ausnahmen und Binding-Details der synthetischen Daten enthalten. Das content-freie produktive Anwendungslog bleibt davon getrennt.
