# Sanctuary Overhauled Lobby

A compact multiplayer lobby for **Sanctuary: Shattered Sun** with host tools, presets, and an optional Overhauled mode.

## Download and install

Download the installer ZIP from [Releases](https://github.com/Killercat4444/Sanctuary-Overhauled-Lobby/releases). Extract it, close Sanctuary, then double-click `installer.bat`. Confirm the Playtest folder and approve the install or update. It preserves presets and settings and uses the Windows PowerShell included with Windows.

| Requirement | Supported version |
| --- | --- |
| Sanctuary: Shattered Sun Playtest | 0.0.1.21.1 |
| Remmy’s Mod Manager | 0.17.0 |
| SCOM | Separate build |

## Features

- **Vanilla mode** includes the compact lobby, presets, and host tools with stock lobby rules.
- **Overhauled mode** adds independent player colors and expanded commander controls. All players need the matching build to start an Overhauled match.
- **Host controls** let you move or swap players, assign teams, kick players, and fill open slots with AI or remove AI in bulk.
- **Lobby presets** let you save, rename, update, and reuse setups.
- **Unit restrictions** let you block land, air, naval, or experimental units, or select individual units. Enabled restrictions require the synchronized gameplay component.

## Report an issue

Open an [issue](https://github.com/Killercat4444/Sanctuary-Overhauled-Lobby/issues/new) and include your Sanctuary, Mod Manager, and lobby versions, the lobby mode, and steps to reproduce. Remove personal information from any logs or screenshots before posting.


## Build and contribute

The C# plugin is in [src](src/), and the mod manifest and Lua hooks are in [package](package/SanctuaryOverhauledLobby/). See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and [CHANGELOG.md](CHANGELOG.md) for version history.

The mod source is available under the [MIT license](LICENSE). Game and dependency binaries are supplied by your local Playtest installation.
