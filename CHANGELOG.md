# Änderungen

## 0.5.2
- Installer: neuer Schalter `-AllUsers` für die Verteilung mit Intune (Win32-App, läuft als SYSTEM): Plugin immer in den Greenshot-Ordner (auch bei 1.3), Kopien in den Benutzerprofilen werden entfernt, keine Rückfragen, Greenshot wird weder beendet noch gestartet. Exit-Codes: 0 = OK, 1618 = Plugin-Datei in Benutzung (Greenshot läuft, Intune versucht es später erneut), 1 = Fehler. Protokoll: `%ProgramData%\HUScroll\Install-HUScroll.log`
- Installer: startet sich in 64-Bit neu, wenn er in einer 32-Bit-PowerShell läuft (sonst wird Greenshot unter `C:\Program Files` nicht gefunden)

## 0.5.1
- Greenshot 1.4: Gehaltenes Tastenkürzel startete das Rezept doppelt (zwei Aufnahmen gleichzeitig, eine davon nur ein stehendes Bild) – jetzt läuft immer nur eine Aufnahme
- Greenshot 1.4: Der Vollbild-Screenshot, den Greenshot vor jedes Tastenkürzel-Rezept setzt, wird wieder freigegeben

## 0.5.0
- Greenshot 1.4: Rezept-Schritt „Scroll-Capture (HUScroll)“ (Parameter Mode = Automatic/Manual) – kombinierbar mit allen anderen Schritten
- Greenshot 1.4: zwei mitgelieferte Rezepte mit Tastenkürzel – Strg+Alt+Druck (automatisch), Strg+Alt+Shift+Druck (manuell), Ergebnis im Editor; Tasten im Rezept-Editor änderbar
- README/LIESMICH: Installationsbefehle mit vollem Pfad, Verweis auf das Greenshot-Repository

## 0.4.1 (Korrekturen aus dem Code-Review)
- Installer: schließt nur die passende Greenshot-Instanz, zuerst normal; ist ein Editor-Bild offen, wird nachgefragt (`-Force` ohne Frage). Rechte werden vor dem Beenden geprüft, Greenshot wird auch nach einem Fehler wieder gestartet, Pfade exakt (`-LiteralPath`)
- Speicher: Das Ergebnis wird direkt ins Bild geschrieben und die gesammelten Zeilen werden danach freigegeben (eine Kopie weniger bei langen Aufnahmen)
- Greenshot-Editor nicht verfügbar: Bild kommt in die Zwischenablage statt verloren zu gehen
- Bilder/Schriften von Menü und Statusfenster werden freigegeben
- Hinweis auf Administrator-Programme, wenn sich nichts bewegt
- Lizenz: Logo-Dateien unter GPLv3, keine Markenrechte an Name und Logo (GPLv3 §7 e); Quellcode-ZIP in jedem Release; LICENSE-LOGO.txt im ZIP
- Build: Greenshot-Referenzen mit SHA-256 geprüft, Schreibrechte nur für den Release-Job
- Tests: dünn besetzte Seiten, Komposition in Blöcken

## 0.4.0
- Neuer Bildvergleich: Raster aus Zellen (32 px × 1 Zeile) statt ganzer Zeilen. Zellen mit bewegtem Inhalt in einem der beiden Bilder werden übersprungen – Video-Hintergründe (z. B. bing.com) und GIFs, die durch jede Zeile laufen, verhindern den Vergleich nicht mehr
- Rückwärts-Suche (Zurückscrollen) nur noch, wenn Vorwärts nicht passt – halbiert die Rechenzeit
- Test: Video-Hintergrund mit unterschiedlichen Masken je Bild

## 0.3.1
- Seiten voller GIFs (z. B. giphy.com): Scrollen und Animation werden jetzt über senkrechte Streifen unterschieden (vorher: Abbruch nach dem ersten Bild)
- Kaum noch ruhiger Inhalt zum Vergleichen: Übergang wird aus der Scrollweite geschätzt statt fälschlich „Ende erreicht“
- Erster Schritt: geschätzte Scrollweite (ca. 100 px je Raste, DPI-abhängig), falls noch nichts gemessen ist

## 0.3.0
- Bewegte Inhalte (GIF, Video, Karussell): werden beim Warten auf ein ruhiges Bild erkannt und beim Vergleich ausgeblendet, im Ergebnis bleiben sie erhalten
- Automatik: kein Abbruch mehr bei unklarem Übergang – der Übergang wird aus der Scrollweite geschätzt (höchstens einmal hintereinander) und am Ende gemeldet
- Manueller Modus: nimmt jetzt auch auf, wenn auf der Seite ständig etwas animiert ist (vorher wartete er auf ein völlig ruhiges Bild)
- Getestet mit Greenshot 1.4.324 (Windows Sandbox)

## 0.2.2
- Lizenz: GPLv3 (wie Greenshot), HU-Logo davon ausgenommen (Assets/LICENSE-LOGO.txt), Lizenzkopf in allen Quelldateien, LICENSE im Release-ZIP
- Installer: PortableApps-Erkennung korrigiert (Ordner App\Greenshot statt greenshot.ini), eine 1.4-Installation löscht nicht mehr das 1.3-Plugin in %APPDATA%

## 0.2.1
- HU-Logo im Tray-Menü, im Auswahl-Overlay, im Statusfenster und als Datei-Symbol der DLL
- README mit Logo und Badges, CHANGELOG

## 0.2.0
- Neuer Modus **Scroll-Capture manuell**: Rahmen ziehen, selbst scrollen, HUScroll nimmt laufend auf
- Zurückscrollen wird erkannt, nichts wird doppelt angehängt
- Roter Rahmen bei fehlendem Anschluss (zu weit auf einmal gescrollt)
- Zwei Menüeinträge: automatisch / manuell

## 0.1.2
- Info-Meldung (mit Windows-Sound) nur noch bei Abbruch oder Problemen
- Stabilere Schrittweite: der größte gemessene Wert pro Mausrad-Raste zählt

## 0.1.1
- Bildvergleich toleranter: Farbrauschen wird ignoriert, eindeutige Treffer zählen auch unter 92 %
- Schrittweite 50 % statt 60 % (mehr Überlappung)

## 0.1.0
- Erste Version: Plugin für Greenshot 1.3 und 1.4, automatisches Scrollen, Zusammensetzen mit Erkennung von Sticky Header/Footer, Scrollbalken und festen Seitenleisten
