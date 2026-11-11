# SquadEditor

**A free, open-source, real-time editor for Football Manager 27.** Open your save, start SquadEditor, and edit players, staff and clubs while the game runs.

[Türkçe](README.tr.md)

> Status: **0.3 (preview)**. Built for FM27 27.0.1 (Steam, Windows). Single-player only. Always back up your save before editing.

## Features

- **Zero setup.** Start the game and load a save: SquadEditor finds it and connects on its own. No layout files, no folders to pick.
- **Logos and faces.** Your graphics packs in `Documents\Sports Interactive\Football Manager 27\graphics` are picked up automatically (the same folder the game uses). Another folder can be set in Settings.
- **Players:** all technical, mental, physical, goalkeeping and hidden attributes, CA / PA, position ratings on a pitch, condition, morale, value, reputation (current, home, world), height, date of birth, wage and contract end.
- **Staff:** managers, coaches, scouts, physios and the rest: coaching, mental, knowledge and medical attributes, personality, CA / PA, reputation, job, wage and contract.
- **Clubs:** squad split into first team and youth teams, staff list, balance, transfer and wage budgets, ticket price, facilities, stadium, reputation and supporters.
- **Rename players and staff, move a player to another club or release him** (free transfer to the senior team, his contract kept). Only what the game's own memory allows is written: a name the game already has, or a new one in place of the old.
- **Search everything** (Ctrl+F) with accent-insensitive matching ("fenerbahce" finds Fenerbahçe). Open players, staff and clubs in tabs.
- **Presets** (Heal and inspire, Wonderkid, World star, Rich owner, max attributes…) on one record or, with right-click, on every selected player of a squad.
- **Undo** every change (Ctrl+Z).
- **Your way of seeing money:** £, €, $ or ₺ with editable exchange rates; wages per week, month or year.
- **Dark and light themes, English and Turkish.**

## Download

Get `SquadEditor-<version>-win-x64.zip` from [Releases](../../releases), unzip, run `SquadEditor.exe`. No installation and no .NET needed.

The build is not code-signed yet, so Windows SmartScreen may warn you: **More info → Run anyway**.

## Usage

1. Start Football Manager 27 and load your save.
2. Start SquadEditor. It connects automatically (a few seconds to read the save).
3. Search a player, staff member or club, change a value and press **Enter**. Arrow keys change numbers by 1, **Esc** cancels, **Ctrl+Z** undoes.
4. Save the game in Football Manager to keep the changes.

The game's own screen may keep showing an old value for a profile that is already open: reopen the profile or press Continue.

## How it works

SquadEditor reads and writes the game's memory (`ReadProcessMemory` / `WriteProcessMemory`). It does not use fixed addresses: game records are found through the classes the game itself defines (MSVC RTTI, e.g. `db::ACTUAL_PLAYER`), so it keeps working when the game moves things around in memory. Field offsets live in a JSON layout (`layouts/fm27.json`) that is built into the exe; a file with the same name in a `layouts` folder next to the exe overrides it.

Reverse-engineering notes: [docs/fm27-notes.md](docs/fm27-notes.md). Some field offsets were cross-checked with the FM27 research published by [FMLibreEditor](https://github.com/tozmoz/FMLibreEditor) and [fm-editor](https://github.com/barak14/fm-editor); thanks to their authors. No code from those projects is used. `tools/FmProbe` is the read-only memory explorer used to map the layout.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build -c Release
dotnet publish src/SquadEditor.App/SquadEditor.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o build
```

## Roadmap

- Signing free agents, moving staff, loans
- Competitions and nations

Contributions are welcome: a pull request with an updated `layouts/fm27.json` is the easiest way to add a field or fix one after a game patch.

## Disclaimer

SquadEditor is an independent project, not affiliated with or endorsed by Sports Interactive or SEGA. *Football Manager* is a trademark of Sports Interactive / SEGA. Use it on single-player saves, at your own risk, and keep backups.

## License

[MIT](LICENSE)
