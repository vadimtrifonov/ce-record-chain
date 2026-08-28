# Skyrim Record Chain

Skyrim Record Chain reports the definition history of requested Skyrim plugin records.

It answers one question for each FormKey:

> Which active plugins define this FormKey, and in what load-order sequence?

## Requirements

- Windows
- .NET 10 runtime
- A Mod Organizer 2 instance and profile

## Usage

Run the tool outside MO2. The tool reads the profile and physical plugin files directly.

Query one FormKey:

```powershell
skyrim-record-chain.exe `
  --game SkyrimSE `
  --mo2-root "C:\Games\Skyrim\My MO2 Instance" `
  --profile Default `
  "03372B:Skyrim.esm"
```

Query FormKeys from a file:

```powershell
skyrim-record-chain.exe `
  --game SkyrimSE `
  --mo2-root "C:\Games\Skyrim\My MO2 Instance" `
  --profile Default `
  --formkeys-from "C:\Path\To\formkeys.txt"
```

Use `--formkeys-from -` to read standard input:

```powershell
Get-Content "C:\Path\To\formkeys.txt" | skyrim-record-chain.exe `
  --game SkyrimSE `
  --mo2-root "C:\Games\Skyrim\My MO2 Instance" `
  --profile Default `
  --formkeys-from -
```

Batch input contains one FormKey per line.

The tool processes FormKeys in input order. It keeps each chain together and orders its rows from origin to winner.

`--game` accepts `SkyrimSE` or `SkyrimVR`. `SkyrimSE` covers Special Edition and Anniversary Edition.

## Profile resolution

For Skyrim SE, the active order combines these sources:

1. The five implicit plugins.
2. Installed plugins from the physical `Skyrim.ccc` file.
3. Enabled, non-ghosted entries from the profile `plugins.txt` file.

For Skyrim VR, the active order combines the six implicit plugins and enabled, non-ghosted `plugins.txt` entries.

For each active plugin, the tool selects the first physical file in this order:

1. The profile Overwrite directory.
2. Enabled managed mods, from strongest to weakest priority.
3. The physical game Data directory.

A missing active plugin or required master causes an error. The `pluginPath` field reports the selected physical file.

## Output

The tool writes compact JSONL to standard output:

```jsonl
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":0,"plugin":"Skyrim.esm","pluginPath":"C:/Games/Skyrim/My MO2 Instance/mods/Updated Masters/Skyrim.esm","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":true,"winner":false}
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":112,"plugin":"Quick Start - SE.esp","pluginPath":"C:/Games/Skyrim/My MO2 Instance/mods/Optional Quick Start/Quick Start - SE.esp","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":false,"winner":true}
```

- `loadOrderIndex` is the zero-based index of the active plugin.
- `origin` marks the first resolved definition. An injected record can originate outside the plugin in `formKey`.
- `winner` marks the final definition. It does not describe a merged container state.
- `majorRecordFlagsRaw` contains all record-header flag bits in one 32-bit unsigned integer.
- `deleted` reports whether the definition is deleted.
- `partial` reports the Partial Form bit for cell, dialog-topic, and worldspace records.

Diagnostics use standard error. An error produces no JSONL output and returns a nonzero exit code.

Batch mode imports and validates the load order once. If one FormKey fails, the tool does not write rows for other FormKeys.

## Limits

Worldspaces, cells, and dialog containers can combine children from several plugins. Include each relevant child FormKey in the query.

## Development

Source builds use [mise](https://mise.jdx.dev/). Mise installs the .NET 10 SDK.

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

The tests generate Skyrim plugins and an isolated MO2 profile with Mutagen.

### Publish

Create the Windows release archive:

```powershell
mise run publish
```

The task writes a folder and ZIP file under `artifacts`.
