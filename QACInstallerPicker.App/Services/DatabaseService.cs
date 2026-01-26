using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QACInstallerPicker.App.Helpers;
using QACInstallerPicker.App.Models;

namespace QACInstallerPicker.App.Services;

public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string? dbPath = null)
    {
        _dbPath = dbPath ?? AppPaths.DatabasePath;
    }

    public async Task InitializeAsync()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS batches (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc TEXT NOT NULL,
    company TEXT,
    helix_version TEXT,
    selected_logical_items TEXT,
    output_root TEXT,
    memo TEXT
);
CREATE TABLE IF NOT EXISTS transfer_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    batch_id INTEGER,
    company TEXT,
    logical_key TEXT,
    asset_source_path TEXT,
    dest_path TEXT,
    size INTEGER,
    status TEXT,
    started_at_utc TEXT,
    completed_at_utc TEXT,
    bytes_copied INTEGER,
    source_last_write_time_utc TEXT,
    source_hash_sha256 TEXT,
    dest_hash_sha256 TEXT,
    verify_result TEXT,
    error_reason TEXT
);
CREATE TABLE IF NOT EXISTS hash_cache (
    source_path TEXT,
    size INTEGER,
    last_write_time_utc TEXT,
    sha256 TEXT,
    calculated_at_utc TEXT,
    PRIMARY KEY (source_path, size, last_write_time_utc)
);
";
        await command.ExecuteNonQueryAsync();

        if (!await ColumnExistsAsync(connection, "batches", "is_hidden"))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE batches ADD COLUMN is_hidden INTEGER NOT NULL DEFAULT 0;";
            await alterCommand.ExecuteNonQueryAsync();
        }

        if (!await ColumnExistsAsync(connection, "transfer_items", "company"))
        {
            var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE transfer_items ADD COLUMN company TEXT;";
            await alterCommand.ExecuteNonQueryAsync();
        }

        await BackfillTransferItemCompaniesAsync(connection);
    }

    public async Task<long> InsertBatchAsync(TransferBatch batch)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO batches (timestamp_utc, company, helix_version, selected_logical_items, output_root, memo)
VALUES ($timestamp, $company, $helix, $selected, $output, $memo);
SELECT last_insert_rowid();
";
        command.Parameters.AddWithValue("$timestamp", batch.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("$company", batch.Company);
        command.Parameters.AddWithValue("$helix", batch.HelixVersion);
        command.Parameters.AddWithValue("$selected", batch.SelectedLogicalItemsJson);
        command.Parameters.AddWithValue("$output", batch.OutputRoot);
        command.Parameters.AddWithValue("$memo", batch.Memo);

        var id = await command.ExecuteScalarAsync();
        return (long)(id ?? 0);
    }

    public async Task<long> InsertTransferItemAsync(TransferItemRecord item)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO transfer_items (
    batch_id, company, logical_key, asset_source_path, dest_path, size, status,
    started_at_utc, completed_at_utc, bytes_copied, source_last_write_time_utc,
    source_hash_sha256, dest_hash_sha256, verify_result, error_reason)
VALUES (
    $batch, $company, $logical, $source, $dest, $size, $status,
    $started, $completed, $bytes, $source_lwt,
    $source_hash, $dest_hash, $verify, $error);
SELECT last_insert_rowid();
";
        command.Parameters.AddWithValue("$batch", item.BatchId);
        command.Parameters.AddWithValue("$company", item.Company);
        command.Parameters.AddWithValue("$logical", item.LogicalKey);
        command.Parameters.AddWithValue("$source", item.AssetSourcePath);
        command.Parameters.AddWithValue("$dest", item.DestPath);
        command.Parameters.AddWithValue("$size", item.Size);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$started", item.StartedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", item.CompletedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$bytes", item.BytesCopied);
        command.Parameters.AddWithValue("$source_lwt", item.SourceLastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$source_hash", item.SourceHashSha256 ?? string.Empty);
        command.Parameters.AddWithValue("$dest_hash", item.DestHashSha256 ?? string.Empty);
        command.Parameters.AddWithValue("$verify", item.VerifyResult.ToString());
        command.Parameters.AddWithValue("$error", item.ErrorReason ?? string.Empty);

        var id = await command.ExecuteScalarAsync();
        return (long)(id ?? 0);
    }

    public async Task UpdateTransferItemAsync(TransferItemRecord item)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE transfer_items SET
    status = $status,
    started_at_utc = $started,
    completed_at_utc = $completed,
    bytes_copied = $bytes,
    source_hash_sha256 = $source_hash,
    dest_hash_sha256 = $dest_hash,
    verify_result = $verify,
    error_reason = $error
WHERE id = $id;
";
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$started", item.StartedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", item.CompletedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$bytes", item.BytesCopied);
        command.Parameters.AddWithValue("$source_hash", item.SourceHashSha256 ?? string.Empty);
        command.Parameters.AddWithValue("$dest_hash", item.DestHashSha256 ?? string.Empty);
        command.Parameters.AddWithValue("$verify", item.VerifyResult.ToString());
        command.Parameters.AddWithValue("$error", item.ErrorReason ?? string.Empty);
        command.Parameters.AddWithValue("$id", item.Id);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> TryGetHashCacheAsync(string sourcePath, long size, DateTime lastWriteUtc)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
SELECT sha256 FROM hash_cache
WHERE source_path = $path AND size = $size AND last_write_time_utc = $lwt;
";
        command.Parameters.AddWithValue("$path", sourcePath);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$lwt", lastWriteUtc.ToString("O"));

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task UpsertHashCacheAsync(HashCacheEntry entry)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO hash_cache (source_path, size, last_write_time_utc, sha256, calculated_at_utc)
VALUES ($path, $size, $lwt, $sha, $calc)
ON CONFLICT(source_path, size, last_write_time_utc)
DO UPDATE SET sha256 = excluded.sha256, calculated_at_utc = excluded.calculated_at_utc;
";
        command.Parameters.AddWithValue("$path", entry.SourcePath);
        command.Parameters.AddWithValue("$size", entry.Size);
        command.Parameters.AddWithValue("$lwt", entry.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$sha", entry.Sha256);
        command.Parameters.AddWithValue("$calc", entry.CalculatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<HistoryItem>> LoadHistoryAsync()
    {
        var items = new List<HistoryItem>();
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
SELECT b.id, b.timestamp_utc, b.company, b.helix_version, b.output_root, b.memo,
       b.selected_logical_items,
       COUNT(t.id) AS item_count
FROM batches b
LEFT JOIN transfer_items t ON t.batch_id = b.id
WHERE COALESCE(b.is_hidden, 0) = 0
GROUP BY b.id
ORDER BY b.timestamp_utc DESC;
";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var selectedJson = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            var itemCount = reader.GetInt32(7);
            if (itemCount == 0 && !string.IsNullOrWhiteSpace(selectedJson))
            {
                itemCount = TryGetItemCountFromJson(selectedJson);
            }

            items.Add(new HistoryItem
            {
                BatchId = reader.GetInt64(0),
                TimestampUtc = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Company = reader.GetString(2),
                HelixVersion = reader.GetString(3),
                OutputRoot = reader.GetString(4),
                Memo = reader.GetString(5),
                ItemCount = itemCount
            });
        }

        return items;
    }

    public async Task<List<TransferItemRecord>> LoadTransferItemsAsync()
    {
        var items = new List<TransferItemRecord>();
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
SELECT t.id, t.batch_id, t.logical_key, t.asset_source_path, t.dest_path, t.size, t.status,
       t.started_at_utc, t.completed_at_utc, t.bytes_copied, t.source_last_write_time_utc,
       t.source_hash_sha256, t.dest_hash_sha256, t.verify_result, t.error_reason,
       COALESCE(t.company, b.company) AS company
FROM transfer_items t
LEFT JOIN batches b ON b.id = t.batch_id
ORDER BY t.id;
";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new TransferItemRecord
            {
                Id = reader.GetInt64(0),
                BatchId = reader.GetInt64(1),
                LogicalKey = reader.GetString(2),
                AssetSourcePath = reader.GetString(3),
                DestPath = reader.GetString(4),
                Size = reader.GetInt64(5),
                Status = Enum.TryParse(reader.GetString(6), out TransferStatus status) ? status : TransferStatus.Paused,
                StartedAtUtc = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletedAtUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                BytesCopied = reader.GetInt64(9),
                SourceLastWriteTimeUtc = DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SourceHashSha256 = reader.GetString(11),
                DestHashSha256 = reader.GetString(12),
                VerifyResult = Enum.TryParse(reader.GetString(13), out VerifyResult verify) ? verify : VerifyResult.NotChecked,
                ErrorReason = reader.GetString(14),
                Company = reader.IsDBNull(15) ? string.Empty : reader.GetString(15)
            });
        }

        return items;
    }

    public async Task ExportHistoryCsvAsync(string path)
    {
        var items = await LoadHistoryAsync();
        var sb = new StringBuilder();
        sb.AppendLine("BatchId,TimestampUtc,Company,HelixVersion,OutputRoot,Memo,ItemCount");
        foreach (var item in items)
        {
            sb.AppendLine($"{item.BatchId},{item.TimestampUtc:O},\"{Escape(item.Company)}\",\"{Escape(item.HelixVersion)}\",\"{Escape(item.OutputRoot)}\",\"{Escape(item.Memo)}\",{item.ItemCount}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public async Task ClearTransferHistoryAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = @"
DELETE FROM transfer_items
WHERE status IN ('Completed', 'Failed', 'Canceled');
";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteHistoryBatchesAsync(IReadOnlyCollection<long> batchIds)
    {
        if (batchIds.Count == 0)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        var parameters = new List<string>();
        var index = 0;
        foreach (var id in batchIds)
        {
            var name = $"$id{index++}";
            parameters.Add(name);
            command.Parameters.AddWithValue(name, id);
        }

        command.CommandText = $"DELETE FROM batches WHERE id IN ({string.Join(", ", parameters)});";
        await command.ExecuteNonQueryAsync();
    }

    public async Task ClearHistoryAsync()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM batches;";
        await command.ExecuteNonQueryAsync();
    }

    private static string Escape(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static int TryGetItemCountFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.GetArrayLength();
            }
        }
        catch
        {
            // Ignore invalid JSON and fall back to zero.
        }

        return 0;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task BackfillTransferItemCompaniesAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE transfer_items
SET company = COALESCE(company, (SELECT company FROM batches WHERE batches.id = transfer_items.batch_id))
WHERE company IS NULL OR company = '';
";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
