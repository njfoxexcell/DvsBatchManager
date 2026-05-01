using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ExcellDvsBatchManager;

public sealed record BatchRow(
    string BatchNumber,
    int TrxCount,
    int StatusCode,
    string StatusText,
    DateTime? CreatedAt,
    DateTime? ModifiedAt,
    string? Comment);

public sealed class BatchRepository
{
    private readonly string _connectionString;

    public BatchRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<BatchRow>> GetPendingDvs7BatchesAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    BatchNumber   = RTRIM(BACHNUMB),
    TrxCount      = NUMOFTRX,
    StatusCode    = BCHSTTUS,
    CreatedAt     = CREATDDT,
    ModifiedAt    = MODIFDT,
    Comment       = RTRIM(BCHCOMNT)
FROM [EXCEL].[dbo].[SY00500] WITH (NOLOCK)
WHERE RTRIM(BCHSOURC) = 'Sales Entry'
  AND BACHNUMB LIKE 'DVS7%'
ORDER BY BACHNUMB;";

        var rows = new List<BatchRow>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandType = CommandType.Text, CommandTimeout = 30 };
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var code = rdr.GetInt16(2);
            rows.Add(new BatchRow(
                BatchNumber: rdr.GetString(0),
                TrxCount:    rdr.GetInt32(1),
                StatusCode:  code,
                StatusText:  StatusLabel(code),
                CreatedAt:   rdr.IsDBNull(3) ? null : rdr.GetDateTime(3),
                ModifiedAt:  rdr.IsDBNull(4) ? null : rdr.GetDateTime(4),
                Comment:     rdr.IsDBNull(5) ? null : rdr.GetString(5)));
        }
        return rows;
    }

    public async Task<int> GetMaxChunkNumberAsync(string chunkBatchPrefix, CancellationToken ct = default)
    {
        // Highest 3-digit suffix already in use for this prefix. Returns 0 if none.
        // Lets us pass @StartingChunkNumber = max + 1 so sibling batches that compute
        // the same prefix (e.g. DVS7-37-0501_12 and DVS7-67-0501_12 both → DVS805012612)
        // don't collide on the proc's chunk-batch-already-exists guard.
        const string sql = @"
SELECT ISNULL(MAX(TRY_CAST(SUBSTRING(RTRIM(BACHNUMB), LEN(@prefix) + 1, 3) AS INT)), 0)
FROM [EXCEL].[dbo].[SY00500] WITH (NOLOCK)
WHERE RTRIM(BCHSOURC) = 'Sales Entry'
  AND LEFT(RTRIM(BACHNUMB), LEN(@prefix)) = @prefix
  AND LEN(RTRIM(BACHNUMB)) = LEN(@prefix) + 3;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandType = CommandType.Text, CommandTimeout = 30 };
        cmd.Parameters.Add("@prefix", SqlDbType.VarChar, 15).Value = chunkBatchPrefix;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int i ? i : 0;
    }

    public async Task SplitBatchAsync(string sourceBatchNumber, string chunkBatchPrefix, int chunkSize, int startingChunkNumber, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("dbo.usp_SplitSOPBatchIntoChunks", conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 300
        };
        cmd.Parameters.Add("@SourceBatchNumber",   SqlDbType.VarChar, 15).Value = sourceBatchNumber;
        cmd.Parameters.Add("@ChunkBatchPrefix",    SqlDbType.VarChar, 15).Value = chunkBatchPrefix;
        cmd.Parameters.Add("@ChunkSize",           SqlDbType.Int).Value         = chunkSize;
        cmd.Parameters.Add("@StartingChunkNumber", SqlDbType.Int).Value         = startingChunkNumber;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string StatusLabel(int code) => code switch
    {
        0 => "Available",
        1 => "Marked",
        2 => "Receiving",
        3 => "Recovering",
        4 => "Edited",
        5 => "Check Printing",
        6 => "Posting",
        7 => "Posting Interrupted",
        8 => "Posting Error",
        9 => "Marked for Posting",
        10 => "Receiving Posting Status",
        _ => $"Status {code}"
    };
}
