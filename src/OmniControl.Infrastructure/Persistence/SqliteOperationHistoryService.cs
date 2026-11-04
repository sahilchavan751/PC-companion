using Microsoft.Data.Sqlite;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.History;
using OmniControl.Core.Results;

namespace OmniControl.Infrastructure.Persistence;

public class SqliteOperationHistoryService : IOperationHistoryService
{
    private readonly string _connectionString;
    private readonly IAdminElevationService _elevationService;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public SqliteOperationHistoryService(IAdminElevationService elevationService)
    {
        _elevationService = elevationService;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
        Directory.CreateDirectory(folder);
        var dbPath = Path.Combine(folder, "OmniControl.db");
        _connectionString = $"Data Source={dbPath}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ChangeHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationName TEXT NOT NULL,
                ModuleName TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                TargetIdentifier TEXT NOT NULL,
                PreviousValue TEXT,
                NewValue TEXT,
                IsSuccess INTEGER NOT NULL,
                ErrorMessage TEXT,
                Privilege INTEGER NOT NULL,
                IsReversible INTEGER NOT NULL,
                ReversalData TEXT
            );

            CREATE TABLE IF NOT EXISTS BackupSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotName TEXT NOT NULL,
                Description TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                Category TEXT NOT NULL,
                PayloadJson TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public async Task RecordOperationAsync(ChangeHistoryRecord record)
    {
        await _dbLock.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ChangeHistory (
                    OperationName, ModuleName, Timestamp, TargetIdentifier, 
                    PreviousValue, NewValue, IsSuccess, ErrorMessage, Privilege, IsReversible, ReversalData
                ) VALUES (
                    @op, @mod, @time, @target, @prev, @new, @succ, @err, @priv, @rev, @revData
                );
            ";

            cmd.Parameters.AddWithValue("@op", record.OperationName);
            cmd.Parameters.AddWithValue("@mod", record.ModuleName);
            cmd.Parameters.AddWithValue("@time", record.Timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@target", record.TargetIdentifier);
            cmd.Parameters.AddWithValue("@prev", (object?)record.PreviousValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@new", (object?)record.NewValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@succ", record.IsSuccess ? 1 : 0);
            cmd.Parameters.AddWithValue("@err", (object?)record.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@priv", (int)record.Privilege);
            cmd.Parameters.AddWithValue("@rev", record.IsReversible ? 1 : 0);
            cmd.Parameters.AddWithValue("@revData", (object?)record.ReversalData ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<ChangeHistoryRecord>> GetHistoryAsync(int limit = 100)
    {
        await _dbLock.WaitAsync();
        try
        {
            var list = new List<ChangeHistoryRecord>();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, OperationName, ModuleName, Timestamp, TargetIdentifier, 
                       PreviousValue, NewValue, IsSuccess, ErrorMessage, Privilege, IsReversible, ReversalData
                FROM ChangeHistory
                ORDER BY Id DESC
                LIMIT @limit;
            ";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ChangeHistoryRecord
                {
                    Id = reader.GetInt64(0),
                    OperationName = reader.GetString(1),
                    ModuleName = reader.GetString(2),
                    Timestamp = DateTime.Parse(reader.GetString(3)),
                    TargetIdentifier = reader.GetString(4),
                    PreviousValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    NewValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsSuccess = reader.GetInt32(7) == 1,
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Privilege = (PrivilegeLevel)reader.GetInt32(9),
                    IsReversible = reader.GetInt32(10) == 1,
                    ReversalData = reader.IsDBNull(11) ? null : reader.GetString(11)
                });
            }

            return list;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<OperationResult> UndoOperationAsync(long recordId)
    {
        var records = await GetHistoryAsync(500);
        var target = records.FirstOrDefault(r => r.Id == recordId);

        if (target == null)
            return OperationResult.Fail($"History record {recordId} not found.");

        if (!target.IsReversible || string.IsNullOrEmpty(target.PreviousValue))
            return OperationResult.Fail("This operation is not marked as reversible or has no previous value.");

        switch (target.ModuleName)
        {
            case "Environment":
                if (target.Privilege == PrivilegeLevel.ElevatedAdmin)
                {
                    return await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
                    {
                        OperationType = AdminOperationType.SetMachineEnvironmentVariable,
                        Parameters =
                        {
                            ["Name"] = target.TargetIdentifier,
                            ["Value"] = target.PreviousValue
                        }
                    });
                }
                else
                {
                    global::System.Environment.SetEnvironmentVariable(target.TargetIdentifier, target.PreviousValue, EnvironmentVariableTarget.User);
                    return OperationResult.Ok($"User environment variable '{target.TargetIdentifier}' restored.");
                }

            case "Services":
                if (Enum.TryParse<Core.Models.Services.WindowsServiceStartMode>(target.PreviousValue, out var mode))
                {
                    return await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
                    {
                        OperationType = AdminOperationType.ChangeServiceStartupType,
                        Parameters =
                        {
                            ["ServiceName"] = target.TargetIdentifier,
                            ["StartMode"] = mode.ToString()
                        }
                    });
                }
                break;
        }

        return OperationResult.Fail($"Automatic undo for module '{target.ModuleName}' is not supported.");
    }

    public async Task<OperationResult> CreateBackupSnapshotAsync(string name, string category, string payloadJson)
    {
        await _dbLock.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BackupSnapshots (SnapshotName, Description, CreatedAt, Category, PayloadJson)
                VALUES (@name, @desc, @time, @cat, @json);
            ";

            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@desc", $"Backup snapshot of {category} settings");
            cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@cat", category);
            cmd.Parameters.AddWithValue("@json", payloadJson);

            await cmd.ExecuteNonQueryAsync();
            return OperationResult.Ok($"Backup snapshot '{name}' created successfully.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to create snapshot: {ex.Message}");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<BackupSnapshot>> GetSnapshotsAsync(string? category = null)
    {
        await _dbLock.WaitAsync();
        try
        {
            var list = new List<BackupSnapshot>();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            if (string.IsNullOrEmpty(category))
            {
                cmd.CommandText = "SELECT Id, SnapshotName, Description, CreatedAt, Category, PayloadJson FROM BackupSnapshots ORDER BY Id DESC;";
            }
            else
            {
                cmd.CommandText = "SELECT Id, SnapshotName, Description, CreatedAt, Category, PayloadJson FROM BackupSnapshots WHERE Category = @cat ORDER BY Id DESC;";
                cmd.Parameters.AddWithValue("@cat", category);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new BackupSnapshot
                {
                    Id = reader.GetInt64(0),
                    SnapshotName = reader.GetString(1),
                    Description = reader.GetString(2),
                    CreatedAt = DateTime.Parse(reader.GetString(3)),
                    Category = reader.GetString(4),
                    PayloadJson = reader.GetString(5)
                });
            }

            return list;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<OperationResult> RestoreSnapshotAsync(long snapshotId)
    {
        var snapshots = await GetSnapshotsAsync();
        var snapshot = snapshots.FirstOrDefault(s => s.Id == snapshotId);

        if (snapshot == null)
            return OperationResult.Fail($"Snapshot with ID {snapshotId} not found.");

        return OperationResult.Ok($"Snapshot '{snapshot.SnapshotName}' loaded ready for verification.");
    }
}
