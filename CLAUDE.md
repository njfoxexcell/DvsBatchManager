# Excell DVS Batch Manager — Claude Code Context

## Project
- **Name:** Excell DVS Batch Manager
- **Repo:** https://github.com/njfoxexcell/DvsBatchManager
- **Local path:** C:/Users/njfox/ExcellDvsBatchManager/
- **Type:** Portable Windows desktop tool (WinForms, .NET 9, single-file self-contained)

## What This App Does
Lists every pending `Sales Entry` batch in Dynamics GP whose `BACHNUMB LIKE 'DVS7%'`, lets the user multi-select, and calls `EXCEL.dbo.usp_SplitSOPBatchIntoChunks` once per selected batch to split each into 100-doc chunks. The new chunk batches are named `DVS8` + `MMDDYY` + last-2-chars-of-source + `NNN` (3-digit chunk suffix).

## Tech Stack
- .NET 9 Windows Forms (`net9.0-windows`), nullable + implicit usings on
- `Microsoft.Data.SqlClient` 5.2.x (Windows Integrated Auth, `TrustServerCertificate=true`)
- Distributed as a single self-contained exe (~51 MB) — no install, no runtime dependency

## DB Connection
- **Server:** `ExcellSQL\ERP`
- **Database:** `EXCEL` (the GP company DB)
- Auth: Windows Integrated. The user running the exe needs read on `SY00500` and execute on `dbo.usp_SplitSOPBatchIntoChunks`.

## Key Files
- [`Program.cs`](Program.cs) — WinForms entry point.
- [`MainForm.cs`](MainForm.cs) — single-form UI: top toolbar (Refresh / Select All / Clear / Mark Completed), `DataGridView` with checkbox column, status strip. Holds the `Server` / `Database` / `SourcePrefix` / `ChunkPrefixRoot` / `ChunkSize` constants — change these and rebuild to retarget.
- [`BatchRepository.cs`](BatchRepository.cs) — two methods: `GetPendingDvs7BatchesAsync` (queries `SY00500` with `NOLOCK`) and `SplitBatchAsync` (calls the proc).
- [`app.manifest`](app.manifest) — long-path-aware; DPI awareness comes from `<ApplicationHighDpiMode>` in csproj.
- [`ExcellDvsBatchManager.csproj`](ExcellDvsBatchManager.csproj) — release config publishes single-file self-contained win-x64 with compression.

## Chunk Prefix Convention
`MainForm.BuildChunkPrefix(sourceBatch)` = `"DVS8"` + `DateTime.Now.ToString("MMddyy")` + `sourceBatch[^2..]`.

Examples (today = 2026-04-24):
- `DVS7-37-0424_6`  → `DVS8042426_6`  (last 2 chars `_6`)
- `DVS7-37-0424_13` → `DVS804242613`  (last 2 chars `13`)
- `DVS7042426001`   → `DVS804242601`  (last 2 chars `01`)

## Stored Procedure Notes
`dbo.usp_SplitSOPBatchIntoChunks` (in `EXCEL`) signature:
```sql
@SourceBatchNumber      CHAR(15),
@SourceBatchSource      CHAR(15)    = 'Sales Entry',
@ChunkBatchPrefix       VARCHAR(12) = 'CHUNK_',   -- ⚠ originally VARCHAR(10)
@ChunkSize              INT         = 100,
@MaxChunks              INT         = 0,
@UserID                 CHAR(15)    = 'sa',
@StartingChunkNumber    INT         = 1,
@HoldToRemove           CHAR(15)    = 'DVSPOST',
@DeleteSourceWhenEmpty  BIT         = 1,
@DryRun                 BIT         = 0,
@Debug                  BIT         = 0
```

Builds new BACHNUMB as `@ChunkBatchPrefix + RIGHT('000' + CAST(@ChunkNum AS VARCHAR(3)), 3)` cast to `CHAR(15)`. Internal length guard `LEN(@ChunkBatchPrefix) + 3 > 15` enforces the 15-char BACHNUMB limit, so the parameter type can safely be widened up to `VARCHAR(12)` without further changes.

**Originally the parameter was `VARCHAR(10)` and silently truncated 12-char prefixes** (e.g. `DVS8042426_6` → `DVS8042426`), producing chunks like `DVS8042426001` instead of `DVS8042426_6001`. If chunks are coming out without the trailing 2-char suffix, verify the proc parameter is at least `VARCHAR(12)`.

Other behavior worth knowing:
- Each chunk is its own atomic transaction; on failure the loop breaks and the source batch is **never** deleted.
- Source batch is deleted only when: `@DeleteSourceWhenEmpty = 1` AND no errors AND no `@MaxChunks` early-stop AND doc-count reconciles AND `SOP10100` shows zero rows for the source.
- `@HoldToRemove = 'DVSPOST'` (default) — process holds with that ID are stripped from the moved docs in `SOP10104`.
- Returns 0 on success, 50001–50008 on validation failures, 50099 on runtime failure. The proc also `SELECT`s a summary row + `@ChunkLog` table at the end; the app uses `ExecuteNonQueryAsync` and ignores those result sets — only exceptions surface.

## Build / Run
```bash
# Debug
dotnet build

# Portable single-file exe (output: dist/ExcellDvsBatchManager.exe)
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -o dist
```

## Working Conventions
- All read queries against the GP DB use `WITH (NOLOCK)`.
- Timestamps are server-local (`DateTime.Now` on the C# side; the proc uses `GETDATE()`).
- Migrations / DDL go in `sql/NNN_description.sql`, idempotent — **the user runs them manually**, not the agent.
- Commit and push after completing a prompt set.
