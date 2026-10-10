<h1 align="center"><img src="Assets/logo64.png" width="44" alt="" align="absmiddle"/> HUScroll</h1>

<p align="center"><b>Scroll-Capture für Greenshot</b><br/>
Lange Webseiten und Fenster als ein Bild – automatisch gescrollt oder selbst gescrollt, zusammengesetzt und im Greenshot-Editor zum Zuschneiden.</p>

<p align="center">
  <a href="https://github.com/ChiliApple/HUScroll/releases/latest"><img src="https://img.shields.io/github/v/release/ChiliApple/HUScroll?label=Version&color=b9a88a" alt="Version"></a>
  <img src="https://img.shields.io/badge/Greenshot-1.3%20%7C%201.4-6DB33F" alt="Greenshot 1.3 | 1.4">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.7.2%20%7C%204.8-512BD4" alt=".NET Framework">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/Lizenz-GPLv3-orange" alt="Lizenz GPLv3">
</p>

<p align="center"><a href="#benutzung">Benutzung</a> · <a href="#installation">Installation</a> · <a href="CHANGELOG.md">Änderungen</a> · <a href="#lizenz">Lizenz</a></p>

---

| Greenshot | Plugin-Variante | Gebaut gegen | Status |
|---|---|---|---|
| 1.3.x | `Greenshot-1.3` (.NET Framework 4.7.2) | 1.3.323 | Hauptvariante |
| 1.4.x | `Greenshot-1.4` (.NET Framework 4.8) | 1.4.324 (Continuous Build) | Plugin-API kann sich noch ändern |

## Benutzung

Rechtsklick aufs Greenshot-Tray-Symbol:

**Scroll-Capture automatisch (HUScroll)** – HUScroll scrollt selbst bis zum Ende

**Scroll-Capture manuell (HUScroll)** – du scrollst selbst mit dem Mausrad; der Rahmen bleibt stehen (durchklickbar), HUScroll nimmt jede Position auf. Zurückscrollen ist erlaubt, angehängt wird erst wieder unterhalb der tiefsten Stelle. Roter Rahmen = kein Anschluss (zu weit auf einmal gescrollt) → etwas zurückscrollen.

Auswahl (beide Modi):

- **Klick**: Fenster bzw. Seiteninhalt unter der Maus (bei Browsern wird der Seitenbereich erkannt, wenn er ein eigenes Fenster-Handle hat)
- **Ziehen**: freier Bereich (Rahmen)
- **Shift** gedrückt halten: ganzes Fenster statt Seiteninhalt
- **Esc** oder **Fertig**: Aufnahme beenden, das Ergebnis bis dahin geht in den Editor

Automatisch hört HUScroll von selbst auf, wenn sich der Inhalt nicht mehr bewegt (Seitenende); beide Modi stoppen bei 30 000 px Höhe oder 120 Bildern.
Eine Info-Meldung gibt es nur bei Abbruch oder Problemen.

## Installation

Release-ZIP entpacken, dann auf dem PC mit Greenshot:

```powershell
# Zielmaschine: der PC, auf dem Greenshot läuft
powershell -ExecutionPolicy Bypass -File .\Install-HUScroll.ps1
```

Das Script erkennt die Greenshot-Version und installiert die passende DLL:

- **1.3**: nach `%APPDATA%\Greenshot\Plugins\Greenshot.Plugin.HUScroll\` (keine Adminrechte nötig)
- **1.4**: nach `<Greenshot-Ordner>\Plugins\Greenshot.Plugin.HUScroll\` – 1.4 lädt Plugins nur aus dem eigenen Ordner; Adminrechte nur nötig, wenn Greenshot unter `C:\Program Files` liegt
- **PortableApps** (Ordner `App\Greenshot` neben der exe): nach `App\Greenshot\Plugins\…`

Eine bestimmte Greenshot-Installation (z. B. die portable 1.4 zum Testen): `Install-HUScroll.ps1 -GreenshotPath "C:\Pfad\Greenshot.exe"`

Deinstallation: `Install-HUScroll.ps1 -Uninstall`

## So funktioniert's

1. **Auswahl**: Vollbild-Overlay auf einem eingefrorenen Screenshot, Fenster unter der Maus werden hervorgehoben (`WindowFinder`, `RegionSelectorForm`).
2. **Scrollen**: Mausrad-Eingaben per `SendInput` in die Mitte des Bereichs, danach wird der Mauszeiger aus dem Bereich geparkt (keine Hover-Effekte). Die Schrittweite passt sich an: ca. 50 % des scrollenden Bereichs pro Schritt, der Rest ist Überlappung.
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

Bauanleitung und Projektaufbau für Entwickler: [docs/BUILD.md](docs/BUILD.md)

## Lizenz

HUScroll ist ein Plugin für Greenshot und nutzt dessen Programmbibliothek `Greenshot.Base.dll`. Greenshot steht unter der GPLv3 – deshalb steht auch HUScroll unter der **[GNU General Public License v3](LICENSE)** (oder einer späteren Version).

- **Programmcode**: GPLv3 – Nutzung, Änderung und Weitergabe nach den Bedingungen der GPLv3
- **HU-Logo** (`Assets/`): nicht Teil der GPLv3, alle Rechte vorbehalten (GPLv3 §7 e) – Details in [Assets/LICENSE-LOGO.txt](Assets/LICENSE-LOGO.txt). Forks müssen das Logo entfernen oder ersetzen.
- **Fremdkomponenten**: Greenshot (GPLv3) und log4net (Apache 2.0) werden nur zum Bauen heruntergeladen und sind **nicht** im Repository oder in den Releases enthalten; zur Laufzeit verwendet HUScroll die Dateien der installierten Greenshot-Version.
