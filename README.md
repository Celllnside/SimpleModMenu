# Megabonk SimpleModMenu

A BepInEx IL2CPP Mod Menu plugin.

## Features
- Infinite Refreshes (force any refresh related checks to succeed)
- Infinite Banishes (optional toggle)
- Infinite Skips (optional toggle)
- Adjustable forced value (default 9999; applied to refreshes / banishes / skips when enabled)
- On‑the‑fly patch reapplication without restarting the game
- Add resources buttons: gold, xp, health
- Collapsible list of every method patched this session (for debugging)
- Aggressive mode: patch all methods that take an `EShopItem` parameter (void/int/bool) with heuristic skipping / return overrides (just dont use if you dont understand.)
- Debug logging toggle (verbose decisions & failures)

## Hotkeys
- `F6` or `F7`: Toggle GUI panel

## Installation
1. Install **BepInEx 6 (IL2CPP)** for your game.
2. Build the project (or download a release binary if provided).
3. Place `SimpleModMenu.dll` into `BepInEx/plugins`.
4. Launch the game once to generate the config file.

## Configuration
Generated at: `BepInEx/config/com.cellinside.SimpleModMenu.cfg`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `EnableInfinite` | bool | true | Master infinite refresh toggle |
| `EnableInfiniteBanishes` | bool | true | Infinite Banish enforcement |
| `EnableInfiniteSkips` | bool | true | Infinite Skip enforcement |
| `ForcedRefreshValue` | int | 9999 | Value forced for enabled features (1 .. 1,000,000) |
| `AggressiveMode` | bool | true | Patch every candidate the scanner finds (may over?patch) |
| `DebugLogging` | bool | false | Verbose patch / runtime logs |

## Troubleshooting
| Issue | Cause | Action |
|-------|-------|--------|
| Counts do not restore | Another mod keeps writing | Disable that mod or turn this mod off earlier |
| Resource buttons do nothing | No inventory captured yet | Enter a run / wait a frame |
| UI flicker / spam | Too many debug logs | Disable `DebugLogging` |
| Game freeze (older builds) | Recursive setter from patched getter | Fixed by current legacy restore pattern |

## Changelog (abridged)
- 1.6.3: GUI, per-feature toggles, adjustable forced value, resource buttons, snapshot restore fix re?applied.

## Credits
- Harmony (patching)
- BepInEx & IL2CPP interop

Enjoy the Mod Menu.