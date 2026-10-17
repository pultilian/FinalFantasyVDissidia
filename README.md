# Final Fantasy V: Dissidia - source

Source code for the compiled parts of the **Final Fantasy V: Dissidia** mod for FINAL FANTASY V
Pixel Remaster (Steam). The mod turns every character x job combination into a different
Final Fantasy hero with its own battle sprites, battle name and Limit Break; the release package
on Nexus Mods also contains the sprite and data files, which are not code and are not in this
repository.

The release zip contains exactly three compiled files, all built from this repository:

| File | Project | What it is |
|---|---|---|
| `FFV Dissidia Mod Manager.exe` | `src/DissidiaRosterEditor` | The installer and settings tool: a .NET Framework 4.8 Windows Forms program. It moves the mod's files into and out of the game folder, edits the mod's own config and CSV files, reads FFV save files (to see which jobs and key items a save holds) and, on uninstall, removes the mod's key items from them. It makes no network connections. |
| `DissidiaNameSwap.dll` | `src/DissidiaNameSwap` | BepInEx plugin: shows the hero's name for the current job in battle and in the job menu, limits the Crystal Shard shop items to one purchase each, and records which shards a save owns. |
| `DissidiaLimit.dll` | `src/DissidiaLimit` | BepInEx plugin: the Limit Break command and gauge (charge by damage taken or by actions, per-limit levels, per-job adjustments, optional MP cost). |

`src/Common/DarkUi.cs` is shared UI code compiled into the Mod Manager.

## Prerequisites

- Windows 10 or 11.
- The .NET SDK (6.0 or newer; 8.0 is what the release was built with): https://dotnet.microsoft.com/download
- For the Mod Manager (net48): the .NET Framework 4.8 targeting pack. It is included in Visual
  Studio 2022 (".NET desktop development" workload) and in the Visual Studio Build Tools; on its
  own it is the ".NET Framework 4.8 Developer Pack".
- For the two plugins (net6.0): a copy of the game with the loader stack the mod runs on, because
  the plugins compile against the game's own assemblies:
  - FINAL FANTASY V Pixel Remaster (Steam)
  - BepInEx 6 Bleeding Edge, Unity IL2CPP, win-x64, build be.735 (from the Memoria.FFPR release
    page: https://github.com/Albeoris/Memoria.FFPR/releases)
  - Run the game once after installing BepInEx: it generates `BepInEx\interop\*.dll`, which the
    plugins reference. (Memoria.FFPR and Magicite are needed to *run* the mod, not to build it.)

## Build

From the repository root:

```
dotnet build FFVDissidia.sln -c Release
```

If the game is not in the default Steam location
(`C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY V PR`), pass its folder:

```
dotnet build FFVDissidia.sln -c Release -p:GameDir="D:\Games\FINAL FANTASY V PR"
```

To build only the Mod Manager (no game needed):

```
dotnet build src/DissidiaRosterEditor/DissidiaRosterEditor.csproj -c Release
```

Outputs:

- `src/DissidiaRosterEditor/bin/Release/DissidiaModManager.exe` - shipped as `FFV Dissidia Mod Manager.exe` at the root of the release zip
- `src/DissidiaNameSwap/bin/Release/DissidiaNameSwap.dll` - shipped at `FFV Dissidia Mod\BepInEx\plugins\`
- `src/DissidiaLimit/bin/Release/DissidiaLimit.dll` - shipped at `FFV Dissidia Mod\BepInEx\plugins\`

The projects use the .NET SDK defaults: no obfuscation, no post-build steps, no package
references beyond the game assemblies listed in the two plugin project files.

## What the code touches

- **Mod Manager**: the game folder (its own files only, under `BepInEx\plugins`,
  `BepInEx\config` and `FINAL FANTASY V_Data\StreamingAssets\{Magicite,Mods}\FFV_Dissidia`),
  its settings in `%AppData%\FFVDissidia\settings.txt`, FFV save files in
  `Documents\My Games\FINAL FANTASY V PR\Steam\<id>\` (read; written only by the explicit
  "remove Crystal Shards" actions, with a copy of each save made in Documents first), and
  backup folders it creates in Documents. See `SaveReader.cs` for the save format handling and
  `Config.cs` for install / update / uninstall.
- **Plugins**: Harmony patches on the game's battle command menu, shop and name display code,
  plus the plugin's own CSV and `.cfg` files in `BepInEx\config`. The name-swap plugin writes
  `BepInEx\config\DissidiaRoster\unlocked.txt`.

## Licence and credits

FINAL FANTASY is a trademark of Square Enix. This is a non-commercial fan project. The mod runs
on BepInEx (BepInEx team), Memoria.FFPR (Albeoris) and Magicite (Silvris).
