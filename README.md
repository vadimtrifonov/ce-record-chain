# Skyrim Record Chain

Skyrim Record Chain reports the definition history of one Skyrim plugin record.

It answers one question:

> Which active plugins define this FormKey, and in what load-order sequence?

Each JSONL row describes one definition. The first row is the origin, and the last row is the winner.

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

```powershell
skyrim-record-chain.cmd `
  --game SkyrimSE `
  --data-folder "C:\Games\Skyrim Special Edition\Data" `
  --load-order "C:\Path\To\plugins.txt" `
  "03372B:Skyrim.esm"
```

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
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":0,"plugin":"Skyrim.esm","pluginPath":"C:/Game/Data/Skyrim.esm","type":"Quest","editorId":"MQ101","majorFlagsRaw":0,"majorFlags":[],"deleted":false,"origin":true,"winner":false}
{"formKey":"03372B:Skyrim.esm","loadOrderIndex":112,"plugin":"Quick Start - SE.esp","pluginPath":"C:/Game/Data/Quick Start - SE.esp","type":"Quest","editorId":"MQ101","majorFlagsRaw":0,"majorFlags":[],"deleted":false,"origin":false,"winner":true}
```

- `loadOrderIndex` is the zero-based index of the active plugin.
- `origin` marks the first resolved definition. An injected record can originate outside `formKey`'s plugin.
- `winner` marks the final definition. It does not describe a merged container state.
- `majorFlagsRaw` preserves every record-header bit.
- `majorFlags` names bits from Mutagen's common Skyrim flag enum.

Record-specific or context-dependent bits can appear only in `majorFlagsRaw`.

Diagnostics use standard error. An operational error produces no JSONL output and returns a nonzero exit code.

## Limits

Worldspaces, cells, and dialog containers can combine children from several plugins. Query each relevant child FormKey separately.

## Development

Run all tests:

```powershell
mise run test
```

The tests generate real Skyrim plugins with Mutagen. They cover overrides, deletion, partial records, containers, ESL records, injected records, and invalid load orders.
