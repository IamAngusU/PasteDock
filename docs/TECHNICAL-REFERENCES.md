# Technische Referenzen

Primärdokumentation für die verwendeten Plattformmechanismen. Diese Links sind Entwicklungsreferenzen, keine Laufzeitverbindungen der App.

- [WPF-Überblick](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [AddClipboardFormatListener](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)
- [Clipboard-Formate und History-/Cloud-Marker](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats)
- [CryptProtectData und Current-User-Schutz](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)
- [SendInput und UIPI](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [OcrEngine](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine)
- [GetDpiForWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
- [DispatcherSynchronizationContext](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatchersynchronizationcontext)

## Ergänzungen für 0.2.0

- .NET-10-Release-Metadaten: https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
- SDK-Installation und Prüfsummen: https://learn.microsoft.com/en-us/dotnet/core/install/windows
- Windows-Zielpakete beim Cross-Build: https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100
- WPF-Visual in Bitmap rendern: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-create-a-bitmap-from-a-visual

Am 7. Oktober 2026 wurde die SDK-Untergrenze 10.0.401 anhand des offiziellen .NET-10-Release-Feeds gewählt. Der Bootstrap löst bei fehlendem SDK die dann aktuelle stabile Version desselben 10.0-Kanals auf. Das ist keine Garantie, dass eine beliebig alte lokale SDK-Installation zukünftig sicher bleibt.

## Ergänzungen für 0.3.0

- [FileAttributes.Temporary](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileattributes) und `NotContentIndexed` für bestmögliche Kennzeichnung temporärer Paste-Dateien
- [PowerToys Advanced Paste](https://learn.microsoft.com/windows/powertoys/advanced-paste) als Produktvergleich für lokale OCR, Plain-Text-/Datei-Paste und optionale Transformationen
- [Windows Clipboard History](https://support.microsoft.com/windows/apps/using-the-clipboard) als Referenz für Grenzen des eingebauten Windows-Verlaufs
- [CopyQ](https://hluk.github.io/CopyQ/), [Ditto](https://ditto-cp.sourceforge.io/), [PasteBar](https://www.pastebar.app/) und [ClipboardFusion](https://www.clipboardfusion.com/Features/) als Vergleichsprodukte für Suche, Organisation, Hotkeys, Regeln und Automatisierung

Diese Produktreferenzen dienen nur dem Funktions-Gap-Audit. PasteDock übernimmt daraus weder Quellcode noch Assets. `scripts/audit-features.py` prüft anschließend, ob für als vorhanden markierte PasteDock-Funktionen tatsächlich eigene Quellanker existieren; das ist kein Laufzeittest.
