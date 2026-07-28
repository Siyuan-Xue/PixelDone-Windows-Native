using Microsoft.Data.Sqlite;
using PixelDone.Core;

namespace PixelDone.Infrastructure;

public sealed class SqliteTodoRepository(string databasePath) : ITodoRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS checklists (
                local_id TEXT NOT NULL PRIMARY KEY,
                sort_index INTEGER NOT NULL,
                name TEXT NOT NULL,
                created_at_millis INTEGER NOT NULL,
                updated_at_millis INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS todo_items (
                local_id TEXT NOT NULL PRIMARY KEY,
                checklist_local_id TEXT NOT NULL,
                sort_index INTEGER NOT NULL DEFAULT 0,
                title TEXT NOT NULL,
                priority TEXT NOT NULL,
                due_at_millis INTEGER NOT NULL,
                completed INTEGER NOT NULL,
                created_at_millis INTEGER NOT NULL,
                updated_at_millis INTEGER NOT NULL,
                reminder_repeat TEXT NOT NULL,
                image_local_name TEXT,
                image_remote_path TEXT,
                trashed_from_checklist_id TEXT,
                trashed_from_checklist_name TEXT,
                trashed_at_millis INTEGER,
                sync_state TEXT NOT NULL DEFAULT 'LOCAL_ONLY',
                remote_id TEXT,
                owner_user_id TEXT,
                remote_version INTEGER,
                last_synced_at_millis INTEGER,
                last_sync_error TEXT,
                FOREIGN KEY(checklist_local_id) REFERENCES checklists(local_id)
            );

            CREATE INDEX IF NOT EXISTS idx_todo_items_checklist
                ON todo_items(checklist_local_id, sort_index);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var seed = connection.CreateCommand();
        seed.CommandText =
            """
            INSERT OR IGNORE INTO checklists
                (local_id, sort_index, name, created_at_millis, updated_at_millis)
            VALUES
                ($id, 0, $name, $now, $now);
            """;
        seed.Parameters.AddWithValue("$id", PixelDoneChecklists.MainId);
        seed.Parameters.AddWithValue("$name", PixelDoneChecklists.MainName);
        seed.Parameters.AddWithValue("$now", now);
        await seed.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TodoItem>> ListAsync(
        string checklistId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<TodoItem>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, checklist_local_id, title, priority, due_at_millis,
                   completed, created_at_millis, updated_at_millis, reminder_repeat,
                   image_local_name, image_remote_path, trashed_from_checklist_id,
                   trashed_from_checklist_name, trashed_at_millis
            FROM todo_items
            WHERE checklist_local_id = $checklist
            ORDER BY sort_index, created_at_millis;
            """;
        command.Parameters.AddWithValue("$checklist", checklistId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TodoItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<TodoPriority>(reader.GetString(3), true),
                reader.GetInt64(4),
                reader.GetBoolean(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                Enum.Parse<ReminderRepeat>(reader.GetString(8), true),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt64(13)));
        }

        return result;
    }

    public async Task UpsertAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO todo_items (
                local_id, checklist_local_id, title, priority, due_at_millis,
                completed, created_at_millis, updated_at_millis, reminder_repeat,
                image_local_name, image_remote_path, trashed_from_checklist_id,
                trashed_from_checklist_name, trashed_at_millis)
            VALUES (
                $id, $checklist, $title, $priority, $due, $completed, $created,
                $updated, $repeat, $imageLocal, $imageRemote, $trashedId,
                $trashedName, $trashedAt)
            ON CONFLICT(local_id) DO UPDATE SET
                checklist_local_id = excluded.checklist_local_id,
                title = excluded.title,
                priority = excluded.priority,
                due_at_millis = excluded.due_at_millis,
                completed = excluded.completed,
                updated_at_millis = excluded.updated_at_millis,
                reminder_repeat = excluded.reminder_repeat,
                image_local_name = excluded.image_local_name,
                image_remote_path = excluded.image_remote_path,
                trashed_from_checklist_id = excluded.trashed_from_checklist_id,
                trashed_from_checklist_name = excluded.trashed_from_checklist_name,
                trashed_at_millis = excluded.trashed_at_millis,
                sync_state = 'DIRTY';
            """;
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$checklist", item.ChecklistId);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$priority", item.Priority.ToString());
        command.Parameters.AddWithValue("$due", item.DueAtMillis);
        command.Parameters.AddWithValue("$completed", item.Completed);
        command.Parameters.AddWithValue("$created", item.CreatedAtMillis);
        command.Parameters.AddWithValue("$updated", item.UpdatedAtMillis);
        command.Parameters.AddWithValue("$repeat", item.ReminderRepeat.ToString());
        command.Parameters.AddWithValue("$imageLocal", (object?)item.ImageLocalName ?? DBNull.Value);
        command.Parameters.AddWithValue("$imageRemote", (object?)item.ImageRemotePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$trashedId", (object?)item.TrashedFromChecklistId ?? DBNull.Value);
        command.Parameters.AddWithValue("$trashedName", (object?)item.TrashedFromChecklistName ?? DBNull.Value);
        command.Parameters.AddWithValue("$trashedAt", (object?)item.TrashedAtMillis ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM todo_items WHERE local_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
