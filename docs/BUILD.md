# HUScroll bauen

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
