# Excell DVS Batch Manager

A portable Windows desktop tool for splitting pending `DVS7*` Sales Entry batches in Dynamics GP (`EXCEL.dbo.SY00500`) into smaller chunks via the `dbo.usp_SplitSOPBatchIntoChunks` stored procedure.

## What It Does

1. Connects to `ExcellSQL\ERP` / `EXCEL` (Windows Auth).
2. Lists every pending `Sales Entry` batch with `BACHNUMB LIKE 'DVS7%'` (batch number, trx count, decoded status, created/modified, comment).
3. User picks one or more batches via checkboxes.
4. **Mark Completed** runs `EXEC dbo.usp_SplitSOPBatchIntoChunks` once per selected batch with:
   - `@SourceBatchNumber` = the selected batch
   - `@ChunkBatchPrefix` = `DVS8` + `MMDDYY` + last 2 chars of source batch
   - `@ChunkSize` = `100`
5. Errors surface in a message box; success refreshes the list.

## Build

Requires .NET 9 SDK on Windows.

```bash
# Debug build
dotnet build

# Portable single-file self-contained exe (~51 MB, no .NET runtime needed)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -o dist
```

The published exe lives at `dist/ExcellDvsBatchManager.exe` and runs on any Windows 10/11 x64 machine without installation.

## Configuration

Server, database, source-batch prefix, chunk-prefix root, and chunk size are constants near the top of [`MainForm.cs`](MainForm.cs). Adjust and rebuild to change.

## Database Notes

- Reads `EXCEL.dbo.SY00500` with `WITH (NOLOCK)`, filtered to `BCHSOURC = 'Sales Entry'` and `BACHNUMB LIKE 'DVS7%'`.
- Writes via `dbo.usp_SplitSOPBatchIntoChunks` only (atomic-per-chunk, with reconciliation guards in the proc itself).
- The proc's `@ChunkBatchPrefix` parameter must be ≥ `VARCHAR(12)` for the `DVS8` + `MMDDYY` + 2-char-suffix prefix to pass through without truncation.
