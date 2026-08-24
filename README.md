# Skyrim Record Chain

Skyrim Record Chain reports the definition history of requested Skyrim plugin records.

It answers one question for each FormKey:

> Which active plugins define this FormKey, and in what load-order sequence?

Each JSONL row describes one definition. For each FormKey, the first row is the origin and the last row is the winner.

## Requirements

- Windows
- .NET 10 runtime

Source builds require [mise](https://mise.jdx.dev/). Mise installs the pinned .NET SDK.

The repository pins .NET and all NuGet packages.

## Build

```powershell
mise trust
mise install
mise run build
```

Create the Windows release archive:

```powershell
mise run publish
```

The task writes version `0.1.0` to a folder and ZIP file under `artifacts`.

## Usage

Query one FormKey:

```powershell
skyrim-record-chain.cmd `
  --game SkyrimSE `
  --data-folder "C:\Games\Skyrim Special Edition\Data" `
  --load-order "C:\Path\To\plugins.txt" `
  "03372B:Skyrim.esm"
```

Query FormKeys from a file:

```powershell
skyrim-record-chain.cmd `
  --game SkyrimSE `
  --data-folder "C:\Games\Skyrim Special Edition\Data" `
  --load-order "C:\Path\To\plugins.txt" `
  --formkeys-from "C:\Path\To\formkeys.txt"
```

Use `--formkeys-from -` to read standard input:

```powershell
Get-Content "C:\Path\To\formkeys.txt" | skyrim-record-chain.cmd `
  --game SkyrimSE `
  --data-folder "C:\Games\Skyrim Special Edition\Data" `
  --load-order "C:\Path\To\plugins.txt" `
  --formkeys-from -
```

The batch input contains one FormKey per line. Empty lines, invalid FormKeys, and duplicate FormKeys cause an error.

The tool processes FormKeys in input order. It keeps each chain together and orders its rows from origin to winner.

`--game` accepts `SkyrimSE` or `SkyrimVR`.

The tool resolves the active load order from these sources:

1. Skyrim implicit plugins
2. Installed entries from `Skyrim.ccc` beside the Data folder
3. Enabled entries from the supplied `plugins.txt`

The tool excludes disabled and ghosted entries. It fails if an active plugin or required master is missing.

Run `skyrim-record-chain.cmd` through the selected MO2 profile when the Data folder uses the MO2 virtual file system.

The launcher starts the managed DLL with `dotnet`. This method is compatible with MO2's USVFS injection.

## Output

The command writes compact JSONL to standard output:

```jsonl
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":0,"plugin":"Skyrim.esm","pluginPath":"C:/Game/Data/Skyrim.esm","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":true,"winner":false}
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":112,"plugin":"Quick Start - SE.esp","pluginPath":"C:/Game/Data/Quick Start - SE.esp","type":"Quest","editorId":"MQ101","majorRecordFlagsRaw":0,"deleted":false,"partial":false,"origin":false,"winner":true}
```

- `loadOrderIndex` is the zero-based index of the active plugin.
- `origin` marks the first resolved definition. An injected record can originate outside `formKey`'s plugin.
- `winner` marks the final definition. It does not describe a merged container state.
- `majorRecordFlagsRaw` contains all record-header flag bits in one 32-bit unsigned integer.
- `deleted` reports whether the definition is deleted.
- `partial` reports the Partial Form bit for cell, dialog-topic, and worldspace records.

Diagnostics use standard error. An error produces no JSONL output and returns a nonzero exit code.

Batch mode imports and validates the load order once. If one FormKey fails, the tool does not write rows for other FormKeys.

## Limits

Worldspaces, cells, and dialog containers can combine children from several plugins. Include each relevant child FormKey in the query.

## Development

Run all tests:

```powershell
mise run test
```

The tests generate real Skyrim plugins with Mutagen. They cover singular and batch queries, overrides, deletion, partial records, containers, ESL records, and injected records.

The tests also cover invalid input and invalid load orders.
