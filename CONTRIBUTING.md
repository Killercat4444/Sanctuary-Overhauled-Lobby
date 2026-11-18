# Build and contribute

The C# project is in `src/`. The installable mod layout is under
`package/SanctuaryOverhauledLobby/`, with the manifest and Lua hooks.

Build on Windows with PowerShell 7 and the supported Sanctuary Playtest
installation. The build uses Roslyn from PowerShell 7, .NET Framework
assemblies from Windows, and the Unity and BepInEx references from your local
game install. Install the supported version of Remmy's Mod Manager first.

From the repository root, pass the engine folder that contains `Sanctuary.exe`:

```powershell
pwsh -NoProfile -File .\Build.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\Sanctuary Shattered Sun Playtest\engine'
```

The script writes the DLL to `build/` and copies it into
`package/SanctuaryOverhauledLobby/`. For local testing, close the game and copy
the completed mod folder into the supported Playtest's `engine\SanctuaryMods`
directory. Keep your existing `presets.ini` and `settings.ini` if you update
a test installation.

The public installer under Releases is for players. It uses Windows
PowerShell 5.1. PowerShell 7 is needed only to run the developer build script.

Keep pull requests focused and describe how you tested them. For multiplayer
changes, use the same DLL and Lua files on every test client and say which
lobby modes and player/AI combinations you exercised.

The MIT license covers the lobby mod's source. Sanctuary, Unity, BepInEx,
Remmy's Mod Manager, and their assets and binaries retain their own terms.
