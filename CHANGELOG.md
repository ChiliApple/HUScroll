# Änderungen

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
