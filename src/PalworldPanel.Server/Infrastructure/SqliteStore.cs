using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class SqliteStore
{
    private static readonly object ProviderLock = new();
    private static bool initialized;
    private readonly string connectionString;
    public string DatabasePath { get; }

    public SqliteStore(string path)
    {
        lock (ProviderLock)
        {
            if (!initialized)
            {
                if (OperatingSystem.IsWindows()) SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
                else
                {
                    NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_sqlite3).Assembly,
                        (name, _, _) => name == "sqlite3" ? NativeLibrary.Load("libsqlite3.so.0") : IntPtr.Zero);
                    SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
                }
                initialized = true;
            }
        }
        DatabasePath = Path.GetFullPath(path);
        SafePaths.RejectLinks(DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, Pooling = true }.ToString();
        using var connection = Open();
        using (var version = connection.CreateCommand())
        {
            version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar()) > 1)
                throw new PanelException("SchemaTooNew", "数据库版本高于程序支持范围，拒绝覆盖。", 503);
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS instances(id TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS tasks(id TEXT PRIMARY KEY, instanceId TEXT NOT NULL,
              kind TEXT NOT NULL, state TEXT NOT NULL, phase TEXT NOT NULL, payload TEXT NOT NULL,
              user TEXT NOT NULL, idempotencyKey TEXT NOT NULL, requestHash TEXT NOT NULL,
              createdUtc TEXT NOT NULL, safeCode TEXT, recoveryPoint TEXT, message TEXT,
              UNIQUE(user,idempotencyKey));
            CREATE UNIQUE INDEX IF NOT EXISTS task_active ON tasks(instanceId)
              WHERE state IN ('Queued','Running','NeedsAttention');
            CREATE TABLE IF NOT EXISTS backups(id TEXT PRIMARY KEY, instanceId TEXT NOT NULL, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS audit(sequence INTEGER PRIMARY KEY AUTOINCREMENT,
              utc TEXT NOT NULL, user TEXT NOT NULL, instanceId TEXT, action TEXT NOT NULL,
              result TEXT NOT NULL, taskId TEXT);
            CREATE TABLE IF NOT EXISTS taskEvents(sequence INTEGER PRIMARY KEY AUTOINCREMENT,
              taskId TEXT NOT NULL, utc TEXT NOT NULL, phase TEXT NOT NULL, state TEXT NOT NULL, code TEXT);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    public IReadOnlyList<InstanceRecord> Instances() => Documents<InstanceRecord>("SELECT document FROM instances ORDER BY id");
    public InstanceRecord Instance(string id) => Instances().FirstOrDefault(i => i.Id == id)
        ?? throw new PanelException("InstanceNotFound", "实例未登记。", 404);
    public void SaveInstance(InstanceRecord instance)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO instances VALUES($id,$doc) ON CONFLICT(id) DO UPDATE SET document=excluded.document";
        command.Parameters.AddWithValue("$id", instance.Id);
        command.Parameters.AddWithValue("$doc", JsonSerializer.Serialize(instance, DurableFile.Json));
        command.ExecuteNonQuery();
    }
    public void ForgetInstance(string id) => Execute("DELETE FROM instances WHERE id=$id", ("$id", id));

    public TaskRecord Enqueue(string instanceId, string kind, string payload, string user, string key, string requestHash)
    {
        if (key.Length is < 8 or > 128) throw new PanelException("InvalidIdempotencyKey", "请求须带有效幂等键。", 400);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = "SELECT * FROM tasks WHERE user=$user AND idempotencyKey=$key";
        existing.Parameters.AddWithValue("$user", user);
        existing.Parameters.AddWithValue("$key", key);
        using (var reader = existing.ExecuteReader())
        {
            if (reader.Read())
            {
                var task = ReadTask(reader);
                if (task.RequestHash != requestHash || task.InstanceId != instanceId || task.Kind != kind)
                    throw new PanelException("IdempotencyConflict", "幂等键已用于不同请求。", 409);
                return task;
            }
        }
        var item = new TaskRecord(Guid.NewGuid().ToString("N"), instanceId, kind, "Queued", "Queued", payload, user, key,
            requestHash, DateTimeOffset.UtcNow.ToString("O"));
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO tasks(id,instanceId,kind,state,phase,payload,user,idempotencyKey,requestHash,createdUtc) VALUES($id,$instance,$kind,'Queued','Queued',$payload,$user,$key,$hash,$utc)";
        insert.Parameters.AddWithValue("$id", item.Id);
        insert.Parameters.AddWithValue("$instance", instanceId);
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$payload", payload);
        insert.Parameters.AddWithValue("$user", user);
        insert.Parameters.AddWithValue("$key", key);
        insert.Parameters.AddWithValue("$hash", requestHash);
        insert.Parameters.AddWithValue("$utc", item.CreatedUtc);
        try { insert.ExecuteNonQuery(); transaction.Commit(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new PanelException("TaskConflict", "此实例已有待执行、执行中或待处理任务。", 409); }
        Audit(user, instanceId, kind, "Queued", item.Id);
        return item;
    }

    public IReadOnlyList<TaskRecord> Tasks(string? state = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = state is null ? "SELECT * FROM tasks ORDER BY createdUtc DESC LIMIT 200" : "SELECT * FROM tasks WHERE state=$state ORDER BY createdUtc";
        if (state is not null) command.Parameters.AddWithValue("$state", state);
        using var reader = command.ExecuteReader();
        var items = new List<TaskRecord>();
        while (reader.Read()) items.Add(ReadTask(reader));
        return items;
    }
    public TaskRecord Task(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tasks WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTask(reader) : throw new PanelException("TaskNotFound", "任务不存在。", 404);
    }

    public TaskRecord? FindRequest(string user, string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM tasks WHERE user=$user AND idempotencyKey=$key";
        command.Parameters.AddWithValue("$user", user);
        command.Parameters.AddWithValue("$key", key);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadTask(reader) : null;
    }

    public bool SetTask(string id, string state, string phase, string? code = null, string? recoveryPoint = null, string? message = null, string? expectedState = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE tasks SET state=$state,phase=$phase,safeCode=$code,recoveryPoint=COALESCE($point,recoveryPoint),message=$message WHERE id=$id AND ($expected IS NULL OR state=$expected);
            """;
        foreach (var p in new (string Key, object? Value)[] { ("$id", id), ("$state", state), ("$phase", phase),
            ("$code", code), ("$point", recoveryPoint), ("$message", message), ("$utc", DateTimeOffset.UtcNow.ToString("O")) })
            command.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$expected", expectedState is null ? DBNull.Value : expectedState);
        if (command.ExecuteNonQuery() != 1) return false;
        command.CommandText = "INSERT INTO taskEvents(taskId,utc,phase,state,code) VALUES($id,$utc,$phase,$state,$code)";
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public void SaveBackup(BackupRecord backup) => Execute("INSERT INTO backups VALUES($id,$instance,$doc)",
        ("$id", backup.Id), ("$instance", backup.InstanceId), ("$doc", JsonSerializer.Serialize(backup, DurableFile.Json)));
    public IReadOnlyList<BackupRecord> Backups(string instanceId) => Documents<BackupRecord>(
        "SELECT document FROM backups WHERE instanceId=$id ORDER BY julianday(json_extract(document, '$.createdUtc')) DESC, id DESC", ("$id", instanceId));
    public IReadOnlyList<BackupRecord> AllBackups() => Documents<BackupRecord>("SELECT document FROM backups ORDER BY julianday(json_extract(document, '$.createdUtc')) DESC, id DESC LIMIT 500");
    public void ForgetBackup(string id) => Execute("DELETE FROM backups WHERE id=$id", ("$id", id));
    public void PruneAudit(DateTimeOffset cutoff) => Execute("""
        DELETE FROM audit WHERE utc<$utc AND (taskId IS NULL OR taskId NOT IN (SELECT id FROM tasks WHERE state IN ('Queued','Running','NeedsAttention')));
        DELETE FROM taskEvents WHERE utc<$utc AND taskId NOT IN (SELECT id FROM tasks WHERE state IN ('Queued','Running','NeedsAttention'));
        """, ("$utc", cutoff.ToString("O")));
    public object[] AuditEvents()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT utc,user,instanceId,action,result,taskId FROM audit ORDER BY rowid DESC LIMIT 200";
        using var reader = command.ExecuteReader();
        var entries = new List<object>();
        while (reader.Read()) entries.Add(new {utc = reader.GetString(0), user = reader.GetString(1),
            instanceId = reader.IsDBNull(2) ? null : reader.GetString(2), action = reader.GetString(3), result = reader.GetString(4),
            taskId = reader.IsDBNull(5) ? null : reader.GetString(5)});
        return entries.ToArray();
    }
    public IReadOnlyList<TaskEvent> Events(string instanceId, long after)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT e.sequence,e.taskId,e.utc,e.phase,e.state,e.code FROM taskEvents e JOIN tasks t ON t.id=e.taskId WHERE t.instanceId=$id AND e.sequence>$after ORDER BY e.sequence LIMIT 100";
        command.Parameters.AddWithValue("$id", instanceId);
        command.Parameters.AddWithValue("$after", after);
        using var reader = command.ExecuteReader();
        var events = new List<TaskEvent>();
        while (reader.Read()) events.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return events;
    }
    public void Audit(string user, string? instanceId, string action, string result, string? taskId = null) => Execute(
        "INSERT INTO audit(utc,user,instanceId,action,result,taskId) VALUES($utc,$user,$instance,$action,$result,$task)",
        ("$utc", DateTimeOffset.UtcNow.ToString("O")), ("$user", user), ("$instance", instanceId), ("$action", action), ("$result", result), ("$task", taskId));

    public void BackupDatabase(string destination)
    {
        SafePaths.RejectLinks(destination);
        if (File.Exists(destination)) throw new PanelException("BackupExists", "数据库快照目标已存在。", 409);
        using var source = Open();
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        target.Open();
        source.BackupDatabase(target);
    }

    private IReadOnlyList<T> Documents<T>(string sql, params (string Key, string? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.AddWithValue(p.Key, (object?)p.Value ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), DurableFile.Json)!);
        return result;
    }

    private void Execute(string sql, params (string Key, string? Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.AddWithValue(p.Key, (object?)p.Value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static TaskRecord ReadTask(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1),
        reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12));
}

public sealed record TaskEvent(long Sequence, string TaskId, string Utc, string Phase, string State, string? Code);
