---
name: ce-record-chain
description: List the plugins that define a record, from origin to winner, by FormKey in Skyrim SE/AE, Skyrim VR or Starfield MO2 profiles.
---

# Creation Engine Record Chain

Use this skill directory as the working directory.

## Setup

```powershell
mise trust mise.toml
mise install
```

## Query

Run outside MO2:

```powershell
mise exec -- ce-record-chain.exe `
  --game <SkyrimSE|SkyrimVR|Starfield> `
  --mo2-root "<folder containing ModOrganizer.ini>" `
  --profile "<profile name>" `
  "<FormKey>"
```

`SkyrimSE` also covers Anniversary Edition.
A FormKey is a local form ID followed by the plugin it belongs to, such as `03372B:Skyrim.esm`.
The ID omits the load-order prefix that the game and xEdit display.

To query several records, replace the FormKey with `--formkeys-from "<path|->"`.
The input contains one FormKey per line, and `-` reads standard input.
Empty or duplicate lines are errors.

## Load order

The tool reads the profile as saved on disk.
Each plugin comes from MO2's Overwrite folder, enabled mods by MO2 priority, or the game's `Data` folder, whichever contains it first.

The load order combines the game's implicit and official plugins, installed plugins from `Skyrim.ccc` (SE only) or `Starfield.ccc`, and enabled `plugins.txt` entries.
In Starfield, each active `<name>` plugin also activates `BlueprintShips-<name>.esm`.
BlueprintShips plugins load last, ordered by their selected files' modification times, oldest first.
Equal timestamps are ordered by descending uppercased filename.

The tool stops with an error for:

- a missing plugin or master;
- a master that loads after a plugin requiring it, unless the master is a BlueprintShips plugin.

## Output

Standard output contains one JSON line for each definition.
Each chain runs from origin to winner, and chains follow the input order.

| Field | Meaning |
|---|---|
| `formKey` | The requested FormKey. |
| `loadOrderIndex` | Zero-based position in the load order. |
| `plugin`, `pluginPath` | The defining plugin and the file that was read. |
| `type` | Mutagen record type, such as `Quest` or `GlobalInt`. |
| `editorId` | Editor ID, or `null`. |
| `majorRecordFlagsRaw` | All record header flags, as an unsigned 32-bit integer. |
| `deleted` | Deleted flag. |
| `partial` | Partial Form flag on CELL, DIAL and WRLD, and on QUST in Starfield. |
| `origin` | First definition; for an injected record, a plugin other than the one in `formKey`. |
| `winner` | Last definition. |

No rows for a FormKey means no active plugin defines it.
On error, the tool exits with a nonzero code, writes to standard error, and writes nothing to standard output.

## Limits

Cells, worldspaces, dialog topics and Starfield quests contain child records from different plugins.
Their rows describe only the container record; query child FormKeys separately.

A record that cannot be decoded fails the whole query.
