# Creation Engine Record Chain

Reports which plugins in a Mod Organizer 2 profile define a record, from the first definition to the winning one.
Supports Skyrim SE/AE, Skyrim VR and Starfield.

## Requirements

- Windows
- .NET 10 runtime

## Usage

```powershell
ce-record-chain.exe `
  --game SkyrimSE `
  --mo2-root "C:\Games\Skyrim\My MO2 Instance" `
  --profile Default `
  "03372B:Skyrim.esm"
```

`--game` is `SkyrimSE`, `SkyrimVR` or `Starfield`.
`--mo2-root` is the MO2 instance folder that contains `ModOrganizer.ini`.
`--profile` is a profile name, not a path.

A FormKey is a local form ID followed by the plugin it belongs to, such as `03372B:Skyrim.esm` or `00C1BA:Starfield.esm`.
The ID omits the load-order prefix that the game and xEdit display.

To query several records, supply one FormKey per line and replace the FormKey argument with:

- `--formkeys-from <path>` to read FormKeys from a file.
- `--formkeys-from -` to read from standard input.

Run the tool outside MO2.
It reads the profile as saved on disk and locates plugin files itself.

## Load order

Each plugin is read from the first location that contains it:

1. MO2's Overwrite folder.
2. Enabled mods, from highest to lowest MO2 priority.
3. The game's `Data` folder.

The tool stops with an error when a plugin or master is missing, or when a master loads after a plugin that requires it.

### Skyrim

Skyrim SE loads:

1. The five implicit plugins: `Skyrim.esm`, `Update.esm` and the three DLC.
2. Installed plugins listed in `Skyrim.ccc` in the game folder.
3. Enabled, non-ghosted `plugins.txt` entries.

Skyrim VR loads its six implicit plugins, including `SkyrimVR.esm`, then enabled, non-ghosted `plugins.txt` entries.

### Starfield

Starfield loads:

1. `Starfield.esm` and the other installed official plugins, in MO2's order.
2. Installed plugins listed in `Starfield.ccc`.
   The file in `Documents\My Games\Starfield` is used when it exists; otherwise, the one in the game folder.
3. Enabled, non-ghosted `plugins.txt` entries.
4. BlueprintShips plugins.

Each active plugin `<name>` activates an installed `BlueprintShips-<name>.esm`, which must have the blueprint flag.
BlueprintShips plugins load after all other plugins, ordered by their selected files' modification times, oldest first.
Equal timestamps are ordered by descending uppercased filename.
Other plugins can use BlueprintShips plugins as masters, even though they load later.
Any other plugin with the blueprint flag is an error, as is a BlueprintShips plugin enabled in `plugins.txt` without an active base plugin.

## Output

Standard output contains one compact JSON line for each definition.
Each chain runs from the first definition to the winner, and chains follow the input order.

```jsonl
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":0,"plugin":"Skyrim.esm","pluginPath":"C:/Games/Skyrim/My MO2 Instance/mods/Updated Masters/Skyrim.esm","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":true,"winner":false}
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":112,"plugin":"Quick Start - SE.esp","pluginPath":"C:/Games/Skyrim/My MO2 Instance/mods/Optional Quick Start/Quick Start - SE.esp","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":false,"winner":true}
```

| Field | Meaning |
|---|---|
| `loadOrderIndex` | Zero-based position in the load order. |
| `pluginPath` | The plugin file that was read. |
| `type` | Mutagen record type, such as `Quest` or `GlobalInt`. |
| `editorId` | Editor ID, or `null` when the definition has none. |
| `majorRecordFlagsRaw` | All record header flags, as an unsigned 32-bit integer. |
| `deleted` | The definition has the Deleted flag. |
| `partial` | The definition has the Partial Form flag. This applies to CELL, DIAL and WRLD, and to QUST in Starfield. |
| `origin` | First definition. For an injected record, this is a plugin other than the one in `formKey`. |
| `winner` | Last definition. |

A FormKey that no active plugin defines produces no rows.
A query without any matches succeeds with empty output.

On error, the tool writes the message to standard error and nothing to standard output.
The exit code is 2 for invalid arguments and 1 for other errors.

## Limits

Cells, worldspaces, dialog topics and Starfield quests contain child records that can come from different plugins.
Their rows describe only the container record, so query child FormKeys separately.

A record that cannot be decoded fails the whole query.

## Development

Source builds use [mise](https://mise.jdx.dev/), which installs the .NET SDK.

### Build

```powershell
mise trust
mise install
mise run build
```

### Test

```powershell
mise run test
```

Tests generate their own plugins and MO2 instances, so they need no game installation.

### Publish

```powershell
mise run publish
```

The task writes the release folder and ZIP file to `artifacts`.
