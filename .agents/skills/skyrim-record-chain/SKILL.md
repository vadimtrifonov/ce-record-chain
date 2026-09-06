---
name: skyrim-record-chain
description: Query active Skyrim record-definition chains by FormKey in SE/AE or VR MO2 profiles.
---

# Skyrim Record Chain

Use this skill directory as the working directory.

## Setup

```bash
mise trust mise.toml
mise install
```

## Query inputs

Each query supplies an explicit game, MO2 root, profile, and FormKey input.

`--game` accepts `SkyrimSE` or `SkyrimVR`. `SkyrimSE` covers Special Edition and Anniversary Edition.
`--mo2-root` identifies the instance directory that contains `ModOrganizer.ini`.
`--profile` identifies a profile by name, not by directory path.

FormKeys use the form `03372B:Skyrim.esm`.

`<FormKey-input>` in the command template means exactly one of:

- `"<FormKey>"`
- `--formkeys-from "<path|->"` for one FormKey per line

`-` reads the CLI process's standard input. Empty, invalid, and duplicate batch entries are errors.

## Run context

Run the command directly, outside MO2.

```powershell
mise exec -- skyrim-record-chain.exe `
  --game <SkyrimSE|SkyrimVR> `
  --mo2-root "<MO2 root>" `
  --profile "<profile>" `
  <FormKey-input>
```

The tool builds the active order from implicit plugins and enabled, non-ghosted profile entries.
For Skyrim SE, it also reads installed entries from the physical `Skyrim.ccc` file.
For each plugin, it selects the first file from `Overwrite`, enabled mods from strongest to weakest, then game `Data`.

## Output

The command writes compact JSONL to standard output.
Each row represents one active plugin definition.
For batch input, rows follow input order and remain contiguous for each FormKey.
Rows in each chain follow active load order from origin to winner.

Each row contains:

- Record identity: `formKey`, `type`, `editorId`
- Provider identity: `loadOrderIndex`, `plugin`, `pluginPath`
- Header state: `majorRecordFlagsRaw`, `deleted`, `partial`
- Chain position: `origin`, `winner`

`majorRecordFlagsRaw` is the raw unsigned 32-bit record-header flag mask.
`origin` marks the first resolved definition. Its provider can differ from the ModKey in `formKey` for an injected record.
`partial` decodes the Partial Form bit for cells, dialog topics, and worldspaces.

Deleted and partial definitions remain in the chain.

A requested FormKey with no active definition produces no row.
A request with no matches succeeds with empty standard output.

Diagnostics use standard error. An error returns a nonzero exit code and leaves standard output empty, including for batch input.

## Limits

Worldspaces, cells, and dialog topics can combine child records from several plugins.
To inspect a child, query its FormKey separately.
