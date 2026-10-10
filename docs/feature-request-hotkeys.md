# Feature request draft (Greenshot GitHub issue)

> Entwurf für ein Issue unter https://github.com/greenshot/greenshot/issues – vor dem Absenden prüfen, ob es schon ein ähnliches Issue gibt.

**Title:** Allow plugins to register hotkeys that are configurable in the settings

**Is your feature request related to a problem?**
Plugins can add entries to the tray menu (`PluginUtils.AddToContextMenu`), but there is no supported way to give these entries a hotkey that the user can configure.
A plugin can call `HotkeyControl.RegisterHotKey` (1.3) / `HotkeyManager.RegisterHotKey` (1.4) itself, but:

1. the hotkey is not visible or changeable in the Greenshot settings, and
2. in 1.3, saving the hotkey settings calls `HotkeyControl.UnregisterHotkeys()`, which removes the hotkeys of all plugins as well.

**Describe the solution you'd like**
- An API for plugins to register a named action with a default hotkey (e.g. `IPluginServices.AddHotkeyAction(id, displayName, defaultHotkey, Action)` in 1.4).
- These actions appear in the hotkey section of the settings, where the user can change or clear them.
- Re-registering the hotkeys after a settings change includes the plugin actions.

**Use case**
A scroll capture plugin (select a window, scroll, stitch, open in the editor) with two modes, automatic and manual. Users would like to start it with e.g. `Ctrl+Alt+Print` like the built-in capture modes.

**Additional context**
Greenshot 1.3.323 and 1.4.324 (continuous build).
