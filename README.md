# HUScroll

Scroll-Capture-Plugin für [Greenshot](https://getgreenshot.org/), nach dem Vorbild der Samsung-Funktion „Scroll-Aufnahme“:
Fenster wählen, HUScroll scrollt selbst, setzt die Bilder zu einem langen Screenshot zusammen und öffnet ihn im Greenshot-Editor, wo du ihn zuschneidest.

| Greenshot | Plugin-Variante | Gebaut gegen | Status |
|---|---|---|---|
| 1.3.x | `Greenshot-1.3` (.NET Framework 4.7.2) | 1.3.323 | Hauptvariante |
| 1.4.x | `Greenshot-1.4` (.NET Framework 4.8) | 1.4.324 (Continuous Build) | Plugin-API kann sich noch ändern |

## Benutzung

Rechtsklick aufs Greenshot-Tray-Symbol → **Scroll-Capture (HUScroll)**

- **Klick**: Fenster bzw. Seiteninhalt unter der Maus (bei Browsern wird der Seitenbereich erkannt, wenn er ein eigenes Fenster-Handle hat)
- **Ziehen**: freier Bereich
- **Shift** gedrückt halten: ganzes Fenster statt Seiteninhalt
- **Esc** oder **Fertig**: Aufnahme beenden, das Ergebnis bis dahin geht in den Editor

HUScroll hört von selbst auf, wenn sich der Inhalt nicht mehr bewegt (Seitenende), bei 30 000 px Höhe oder 120 Bildern.

## Installation

Release-ZIP entpacken, dann auf dem PC mit Greenshot:

```powershell
# Zielmaschine: der PC, auf dem Greenshot läuft
powershell -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1
```

Das Script erkennt die Greenshot-Version und installiert die passende DLL:

- **1.3**: nach `%APPDATA%\Greenshot\Plugins\Greenshot.Plugin.HUScroll\` (keine Adminrechte nötig)
- **1.4**: nach `<Greenshot-Ordner>\Plugins\Greenshot.Plugin.HUScroll\` (Adminrechte nötig, 1.4 lädt Plugins nur aus dem Programmordner)

Deinstallation: `Install-HUScroll.ps1 -Uninstall`

## So funktioniert's

1. **Auswahl**: Vollbild-Overlay auf einem eingefrorenen Screenshot, Fenster unter der Maus werden hervorgehoben (`WindowFinder`, `RegionSelectorForm`).
2. **Scrollen**: Mausrad-Eingaben per `SendInput` in die Mitte des Bereichs, danach wird der Mauszeiger aus dem Bereich geparkt (keine Hover-Effekte). Die Schrittweite passt sich an: ca. 60 % des scrollenden Bereichs pro Schritt, der Rest ist Überlappung.
3. **Warten**: nach jedem Schritt wird so lange aufgenommen, bis zwei Aufnahmen identisch sind (Smooth-Scrolling, Lazy Loading).
4. **Zusammensetzen** (`StitchEngine`, plattformunabhängig und getestet):
   - Fixe Kopf-/Fußzeilen (Sticky Header, Browser-Leisten) werden erkannt und nur einmal übernommen.
   - Scrollbalken (rechter Rand) und feste Seitenleisten werden beim Vergleich ignoriert.
   - Die Verschiebung wird über Zeilen-Hashes gesucht, nur neue Zeilen werden angehängt.
5. **Übergabe**: Greenshot-Surface mit dem Bild → Ziel `Editor`.

## Bekannte Grenzen

- Inhalte, die sich beim Scrollen verändern (Animationen, Videos, Werbung), können zum Abbruch führen („Bilder passten nicht zusammen“) – das Ergebnis bis dahin bleibt erhalten.
- Seiten mit eigenem Scroll-Container: die Maus muss beim Start über dem scrollenden Bereich sein (Mitte des gewählten Bereichs).
- Programme, die als Administrator laufen, nehmen von Greenshot (ohne Adminrechte) keine Mausrad-Eingaben an.
- Fixe Elemente mitten im Inhalt (z. B. schwebende Buttons) erscheinen mehrfach – im Editor wegschneiden.

## Bauen

```powershell
# Zielmaschine: Build-PC (Windows) mit .NET SDK 8
.\tools\Get-GreenshotRefs.ps1     # Greenshot-DLLs nach .\lib laden (nicht im Repo)
dotnet test tests\HUScroll.Tests
dotnet build src\Greenshot.Plugin.HUScroll.V13 -c Release
dotnet build src\Greenshot.Plugin.HUScroll.V14 -c Release
```

GitHub Actions baut bei jedem Push beide Varianten (Artefakt `HUScroll-<Version>`), ein Tag `v*` erzeugt ein Release mit ZIP.

```
src/HUScroll.Core/                  Auswahl, Scrollen, Zusammensetzen (kennt Greenshot nicht)
src/Greenshot.Plugin.HUScroll.V13/  Anbindung Greenshot 1.3 (Tray-Menü, Editor)
src/Greenshot.Plugin.HUScroll.V14/  Anbindung Greenshot 1.4 (Tray-Menü, Editor)
tests/HUScroll.Tests/               Tests der StitchEngine (laufen auch unter Linux)
tools/                              Referenzen laden, Installation
```
