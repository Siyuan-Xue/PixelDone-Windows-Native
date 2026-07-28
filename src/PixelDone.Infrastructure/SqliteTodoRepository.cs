using Microsoft.Data.Sqlite;
using PixelDone.Core;

namespace PixelDone.Infrastructure;

public sealed class SqliteTodoRepository(string databasePath) : ITodoRepository, ISyncRepository
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    // This is the clean native-client baseline. It is not an importer or a
    // legacy PixelDone schema upgrade; future released schemas append entries.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE checklists (
            local_id TEXT NOT NULL PRIMARY KEY,
            sort_index INTEGER NOT NULL,
            name TEXT NOT NULL,
            created_at_millis INTEGER NOT NULL,
            updated_at_millis INTEGER NOT NULL,
            sync_state TEXT NOT NULL DEFAULT 'LocalOnly',
            remote_id TEXT,
            owner_user_id TEXT,
            remote_version INTEGER,
            deleted_at_millis INTEGER
        );

        CREATE TABLE todo_items (
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
            sync_state TEXT NOT NULL DEFAULT 'LocalOnly',
            remote_id TEXT,
            owner_user_id TEXT,
            remote_version INTEGER,
            last_synced_at_millis INTEGER,
            last_sync_error TEXT,
            FOREIGN KEY(checklist_local_id) REFERENCES checklists(local_id)
        );

        CREATE TABLE todo_attachments (
            local_id TEXT NOT NULL PRIMARY KEY,
            todo_local_id TEXT NOT NULL UNIQUE,
            local_path TEXT,
            remote_path TEXT,
            sha256 TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            byte_size INTEGER NOT NULL,
            updated_at_millis INTEGER NOT NULL,
            sync_state TEXT NOT NULL DEFAULT 'LocalOnly',
            remote_version INTEGER,
            deleted_at_millis INTEGER,
            FOREIGN KEY(todo_local_id) REFERENCES todo_items(local_id) ON DELETE CASCADE
        );

        CREATE TABLE app_settings (
            settings_id INTEGER NOT NULL PRIMARY KEY CHECK(settings_id = 1),
            theme TEXT NOT NULL,
            language TEXT NOT NULL,
            sort_mode TEXT NOT NULL,
            show_ddl INTEGER NOT NULL,
            hide_completed INTEGER NOT NULL,
            quick_delete INTEGER NOT NULL,
            update_prompts INTEGER NOT NULL,
            enhanced_xhigh_alarm INTEGER NOT NULL,
            dock_actions TEXT NOT NULL,
            updated_at_millis INTEGER NOT NULL,
            sync_state TEXT NOT NULL DEFAULT 'LocalOnly',
            remote_version INTEGER,
            last_synced_at_millis INTEGER,
            last_sync_error TEXT
        );

        CREATE TABLE sync_metadata (
            metadata_id INTEGER NOT NULL PRIMARY KEY CHECK(metadata_id = 1),
            owner_user_id TEXT,
            cursor INTEGER NOT NULL DEFAULT 0,
            schema_version TEXT NOT NULL DEFAULT '3.2',
            last_sync_at_millis INTEGER,
            last_error TEXT
        );

        CREATE TABLE sync_conflicts (
            conflict_id TEXT NOT NULL PRIMARY KEY,
            record_type TEXT NOT NULL,
            local_id TEXT NOT NULL,
            fields_json TEXT NOT NULL,
            local_json TEXT NOT NULL,
            cloud_json TEXT NOT NULL,
            created_at_millis INTEGER NOT NULL,
            resolved_at_millis INTEGER
        );

        CREATE TABLE sync_tombstones (
            tombstone_id TEXT NOT NULL PRIMARY KEY,
            record_type TEXT NOT NULL,
            local_id TEXT NOT NULL,
            deleted_at_millis INTEGER NOT NULL,
            remote_version INTEGER,
            sync_state TEXT NOT NULL DEFAULT 'Dirty'
        );

        CREATE TABLE pending_mutations (
            mutation_id TEXT NOT NULL PRIMARY KEY,
            record_type TEXT NOT NULL,
            local_id TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            created_at_millis INTEGER NOT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0,
            last_error TEXT
        );

        CREATE TABLE reminder_deliveries (
            todo_local_id TEXT NOT NULL,
            occurrence_at_millis INTEGER NOT NULL,
            delivered_at_millis INTEGER NOT NULL,
            PRIMARY KEY(todo_local_id, occurrence_at_millis),
            FOREIGN KEY(todo_local_id) REFERENCES todo_items(local_id) ON DELETE CASCADE
        );

        CREATE INDEX idx_checklists_sort
            ON checklists(deleted_at_millis, sort_index);
        CREATE INDEX idx_todo_items_checklist
            ON todo_items(checklist_local_id, trashed_at_millis, sort_index);
        CREATE INDEX idx_todo_items_sync
            ON todo_items(sync_state, updated_at_millis);
        CREATE INDEX idx_conflicts_open
            ON sync_conflicts(resolved_at_millis, created_at_millis);
        CREATE INDEX idx_tombstones_sync
            ON sync_tombstones(sync_state, deleted_at_millis);
        """,
        """
        ALTER TABLE app_settings
        ADD COLUMN dock_plus_placement TEXT NOT NULL DEFAULT 'Center';
        """,
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await ExecuteAsync(
                connection,
                """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_at_millis INTEGER NOT NULL
                );
                """,
                cancellationToken);

            var currentVersion = await ScalarLongAsync(
                connection,
                "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;",
                cancellationToken);

            if (currentVersion == Migrations.Length &&
                (!await ColumnExistsAsync(
                    connection,
                    "app_settings",
                    "remote_version",
                    cancellationToken) ||
                 !await TableExistsAsync(
                     connection,
                     "reminder_deliveries",
                     cancellationToken)))
            {
                await ExecuteAsync(
                    connection,
                    """
                    PRAGMA foreign_keys = OFF;
                    DROP TABLE IF EXISTS reminder_deliveries;
                    DROP TABLE IF EXISTS pending_mutations;
                    DROP TABLE IF EXISTS sync_tombstones;
                    DROP TABLE IF EXISTS sync_conflicts;
                    DROP TABLE IF EXISTS sync_metadata;
                    DROP TABLE IF EXISTS app_settings;
                    DROP TABLE IF EXISTS todo_attachments;
                    DROP TABLE IF EXISTS todo_items;
                    DROP TABLE IF EXISTS checklists;
                    DELETE FROM schema_migrations;
                    PRAGMA foreign_keys = ON;
                    """,
                    cancellationToken);
                currentVersion = 0;
            }

            for (var index = (int)currentVersion; index < Migrations.Length; index++)
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var migration = connection.CreateCommand();
                migration.Transaction = (SqliteTransaction)transaction;
                migration.CommandText = Migrations[index];
                await migration.ExecuteNonQueryAsync(cancellationToken);

                await using var marker = connection.CreateCommand();
                marker.Transaction = (SqliteTransaction)transaction;
                marker.CommandText =
                    """
                    INSERT INTO schema_migrations(version, applied_at_millis)
                    VALUES ($version, $applied);
                    """;
                marker.Parameters.AddWithValue("$version", index + 1);
                marker.Parameters.AddWithValue(
                    "$applied",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await marker.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            await SeedAsync(connection, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<TodoChecklist>> ListChecklistsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<TodoChecklist>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, name, sort_index, created_at_millis, updated_at_millis,
                   sync_state, remote_id, owner_user_id, remote_version,
                   deleted_at_millis
            FROM checklists
            WHERE deleted_at_millis IS NULL
            ORDER BY
                CASE local_id
                    WHEN 'trash' THEN 1
                    WHEN 'settings' THEN 2
                    ELSE 0
                END,
                sort_index,
                created_at_millis;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadChecklist(reader));
        }

        return result;
    }

    public Task UpsertChecklistAsync(
        TodoChecklist checklist,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO checklists (
                        local_id, sort_index, name, created_at_millis,
                        updated_at_millis, sync_state, remote_id, owner_user_id,
                        remote_version, deleted_at_millis)
                    VALUES (
                        $id, $sort, $name, $created, $updated, $sync, $remote,
                        $owner, $version, $deleted)
                    ON CONFLICT(local_id) DO UPDATE SET
                        sort_index = excluded.sort_index,
                        name = excluded.name,
                        updated_at_millis = excluded.updated_at_millis,
                        sync_state = excluded.sync_state,
                        remote_id = excluded.remote_id,
                        owner_user_id = excluded.owner_user_id,
                        remote_version = excluded.remote_version,
                        deleted_at_millis = excluded.deleted_at_millis;
                    """;
                AddChecklistParameters(command, checklist);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

    public Task DeleteChecklistAsync(
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteTransactionAsync(
            async (connection, transaction) =>
            {
                if (checklistId is PixelDoneChecklists.TrashId or PixelDoneChecklists.SettingsId)
                {
                    return;
                }

                var normalCount = await ScalarLongAsync(
                    connection,
                    """
                    SELECT COUNT(*) FROM checklists
                    WHERE deleted_at_millis IS NULL
                      AND local_id NOT IN ('trash', 'settings');
                    """,
                    cancellationToken,
                    transaction);
                if (normalCount <= 1)
                {
                    throw new InvalidOperationException("PixelDone must keep at least one checklist.");
                }

                var checklistName = await ScalarStringAsync(
                    connection,
                    "SELECT name FROM checklists WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", checklistId)) ?? "RESTORED";

                await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_items
                    SET checklist_local_id = 'trash',
                        trashed_from_checklist_id = $id,
                        trashed_from_checklist_name = $name,
                        trashed_at_millis = $now,
                        updated_at_millis = $now,
                        sync_state = 'Dirty'
                    WHERE checklist_local_id = $id
                      AND trashed_at_millis IS NULL;
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", checklistId),
                    ("$name", checklistName),
                    ("$now", nowMillis));

                await ExecuteAsync(
                    connection,
                    "DELETE FROM checklists WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", checklistId));

                await InsertTombstoneAsync(
                    connection,
                    transaction,
                    SyncRecordType.Checklist,
                    checklistId,
                    nowMillis,
                    cancellationToken);
            },
            cancellationToken);

    public async Task<IReadOnlyList<TodoItem>> ListAsync(
        string checklistId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<TodoItem>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = checklistId == PixelDoneChecklists.TrashId
            ? """
              SELECT local_id, checklist_local_id, title, priority, due_at_millis,
                     completed, created_at_millis, updated_at_millis, reminder_repeat,
                     image_local_name, image_remote_path, trashed_from_checklist_id,
                     trashed_from_checklist_name, trashed_at_millis, sort_index,
                     sync_state, remote_id, owner_user_id, remote_version,
                     last_synced_at_millis, last_sync_error
              FROM todo_items
              WHERE trashed_at_millis IS NOT NULL
              ORDER BY trashed_at_millis DESC, created_at_millis;
              """
            : """
              SELECT local_id, checklist_local_id, title, priority, due_at_millis,
                     completed, created_at_millis, updated_at_millis, reminder_repeat,
                     image_local_name, image_remote_path, trashed_from_checklist_id,
                     trashed_from_checklist_name, trashed_at_millis, sort_index,
                     sync_state, remote_id, owner_user_id, remote_version,
                     last_synced_at_millis, last_sync_error
              FROM todo_items
              WHERE checklist_local_id = $checklist
                AND trashed_at_millis IS NULL
              ORDER BY sort_index, created_at_millis;
              """;
        if (checklistId != PixelDoneChecklists.TrashId)
        {
            command.Parameters.AddWithValue("$checklist", checklistId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadTodo(reader));
        }

        return result;
    }

    public async Task<TodoItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, checklist_local_id, title, priority, due_at_millis,
                   completed, created_at_millis, updated_at_millis, reminder_repeat,
                   image_local_name, image_remote_path, trashed_from_checklist_id,
                   trashed_from_checklist_name, trashed_at_millis, sort_index,
                   sync_state, remote_id, owner_user_id, remote_version,
                   last_synced_at_millis, last_sync_error
            FROM todo_items
            WHERE local_id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTodo(reader) : null;
    }

    public Task UpsertAsync(
        TodoItem item,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO todo_items (
                        local_id, checklist_local_id, sort_index, title, priority,
                        due_at_millis, completed, created_at_millis, updated_at_millis,
                        reminder_repeat, image_local_name, image_remote_path,
                        trashed_from_checklist_id, trashed_from_checklist_name,
                        trashed_at_millis, sync_state, remote_id, owner_user_id,
                        remote_version, last_synced_at_millis, last_sync_error)
                    VALUES (
                        $id, $checklist, $sort, $title, $priority, $due, $completed,
                        $created, $updated, $repeat, $imageLocal, $imageRemote,
                        $trashedId, $trashedName, $trashedAt, $sync, $remote,
                        $owner, $version, $lastSynced, $lastError)
                    ON CONFLICT(local_id) DO UPDATE SET
                        checklist_local_id = excluded.checklist_local_id,
                        sort_index = excluded.sort_index,
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
                        sync_state = excluded.sync_state,
                        remote_id = excluded.remote_id,
                        owner_user_id = excluded.owner_user_id,
                        remote_version = excluded.remote_version,
                        last_synced_at_millis = excluded.last_synced_at_millis,
                        last_sync_error = excluded.last_sync_error;
                    """;
                AddTodoParameters(command, item);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

    public Task DeleteAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteTransactionAsync(
            async (connection, transaction) =>
            {
                var checklistName = await ScalarStringAsync(
                    connection,
                    """
                    SELECT c.name
                    FROM todo_items t
                    JOIN checklists c ON c.local_id = t.checklist_local_id
                    WHERE t.local_id = $id;
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", id)) ?? PixelDoneChecklists.MainName;

                await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_items
                    SET trashed_from_checklist_id = checklist_local_id,
                        trashed_from_checklist_name = $name,
                        checklist_local_id = 'trash',
                        trashed_at_millis = $now,
                        updated_at_millis = $now,
                        sync_state = 'Dirty'
                    WHERE local_id = $id;
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", id),
                    ("$name", checklistName),
                    ("$now", nowMillis));
            },
            cancellationToken);

    public Task RestoreAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteTransactionAsync(
            async (connection, transaction) =>
            {
                var originId = await ScalarStringAsync(
                    connection,
                    "SELECT trashed_from_checklist_id FROM todo_items WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", id)) ?? PixelDoneChecklists.MainId;
                var originName = await ScalarStringAsync(
                    connection,
                    "SELECT trashed_from_checklist_name FROM todo_items WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", id)) ?? "RESTORED";

                if (originId is PixelDoneChecklists.TrashId or PixelDoneChecklists.SettingsId)
                {
                    originId = PixelDoneChecklists.MainId;
                    originName = PixelDoneChecklists.MainName;
                }

                await ExecuteAsync(
                    connection,
                    """
                    INSERT OR IGNORE INTO checklists (
                        local_id, sort_index, name, created_at_millis,
                        updated_at_millis, sync_state)
                    VALUES (
                        $id,
                        (SELECT COALESCE(MAX(sort_index), -1) + 1 FROM checklists),
                        $name, $now, $now, 'Dirty');
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", originId),
                    ("$name", originName),
                    ("$now", nowMillis));

                await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_items
                    SET checklist_local_id = $checklist,
                        trashed_from_checklist_id = NULL,
                        trashed_from_checklist_name = NULL,
                        trashed_at_millis = NULL,
                        updated_at_millis = $now,
                        sync_state = 'Dirty'
                    WHERE local_id = $id;
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", id),
                    ("$checklist", originId),
                    ("$now", nowMillis));
            },
            cancellationToken);

    public Task MoveToChecklistAsync(
        string id,
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                if (checklistId is PixelDoneChecklists.TrashId or PixelDoneChecklists.SettingsId)
                {
                    throw new InvalidOperationException("Choose a normal checklist.");
                }

                var exists = await ScalarLongAsync(
                    connection,
                    """
                    SELECT EXISTS(
                        SELECT 1 FROM checklists
                        WHERE local_id = $checklist AND deleted_at_millis IS NULL);
                    """,
                    cancellationToken,
                    parameters: [("$checklist", checklistId)]);
                if (exists == 0)
                {
                    throw new InvalidOperationException("The target checklist was not found.");
                }

                var sortIndex = await ScalarLongAsync(
                    connection,
                    """
                    SELECT COALESCE(MAX(sort_index), -1) + 1
                    FROM todo_items WHERE checklist_local_id = $checklist;
                    """,
                    cancellationToken,
                    parameters: [("$checklist", checklistId)]);
                await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_items
                    SET checklist_local_id = $checklist, sort_index = $sort,
                        updated_at_millis = $now, sync_state = 'Dirty'
                    WHERE local_id = $id AND trashed_at_millis IS NULL;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$id", id),
                        ("$checklist", checklistId),
                        ("$sort", sortIndex),
                        ("$now", nowMillis),
                    ]);
            },
            cancellationToken);

    public Task DeletePermanentlyAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteTransactionAsync(
            async (connection, transaction) =>
            {
                await InsertTombstoneAsync(
                    connection,
                    transaction,
                    SyncRecordType.Todo,
                    id,
                    nowMillis,
                    cancellationToken);
                await ExecuteAsync(
                    connection,
                    "DELETE FROM todo_items WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", id));
            },
            cancellationToken);

    public async Task<int> MoveCompletedToTrashAsync(
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default)
    {
        var changed = 0;
        await WriteTransactionAsync(
            async (connection, transaction) =>
            {
                var checklistName = await ScalarStringAsync(
                    connection,
                    "SELECT name FROM checklists WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", checklistId)) ?? PixelDoneChecklists.MainName;

                changed = await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_items
                    SET trashed_from_checklist_id = checklist_local_id,
                        trashed_from_checklist_name = $name,
                        checklist_local_id = 'trash',
                        trashed_at_millis = $now,
                        updated_at_millis = $now,
                        sync_state = 'Dirty'
                    WHERE checklist_local_id = $id
                      AND completed = 1
                      AND trashed_at_millis IS NULL;
                    """,
                    cancellationToken,
                    transaction,
                    ("$id", checklistId),
                    ("$name", checklistName),
                    ("$now", nowMillis));
            },
            cancellationToken);
        return changed;
    }

    public async Task<int> MoveManyToTrashAsync(
        string checklistId,
        IReadOnlyCollection<string> ids,
        long nowMillis,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        await WriteTransactionAsync(
            async (connection, transaction) =>
            {
                var checklistName = await ScalarStringAsync(
                    connection,
                    "SELECT name FROM checklists WHERE local_id = $id;",
                    cancellationToken,
                    transaction,
                    ("$id", checklistId)) ?? PixelDoneChecklists.MainName;

                foreach (var id in ids.Distinct(StringComparer.Ordinal))
                {
                    changed += await ExecuteAsync(
                        connection,
                        """
                        UPDATE todo_items
                        SET trashed_from_checklist_id = checklist_local_id,
                            trashed_from_checklist_name = $name,
                            checklist_local_id = 'trash',
                            trashed_at_millis = $now,
                            updated_at_millis = $now,
                            sync_state = 'Dirty'
                        WHERE local_id = $todo
                          AND checklist_local_id = $checklist
                          AND trashed_at_millis IS NULL;
                        """,
                        cancellationToken,
                        transaction,
                        ("$todo", id),
                        ("$checklist", checklistId),
                        ("$name", checklistName),
                        ("$now", nowMillis));
                }
            },
            cancellationToken);
        return changed;
    }

    public async Task<int> PurgeTrashAsync(
        long nowMillis,
        CancellationToken cancellationToken = default)
    {
        var changed = 0;
        await WriteTransactionAsync(
            async (connection, transaction) =>
            {
                var ids = new List<string>();
                await using (var query = connection.CreateCommand())
                {
                    query.Transaction = transaction;
                    query.CommandText =
                        "SELECT local_id FROM todo_items WHERE trashed_at_millis IS NOT NULL;";
                    await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        ids.Add(reader.GetString(0));
                    }
                }

                foreach (var id in ids)
                {
                    await InsertTombstoneAsync(
                        connection,
                        transaction,
                        SyncRecordType.Todo,
                        id,
                        nowMillis,
                        cancellationToken);
                }

                changed = await ExecuteAsync(
                    connection,
                    "DELETE FROM todo_items WHERE trashed_at_millis IS NOT NULL;",
                    cancellationToken,
                    transaction);
            },
            cancellationToken);
        return changed;
    }

    public async Task<PixelDoneSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT theme, language, sort_mode, show_ddl, hide_completed,
                   quick_delete, update_prompts, enhanced_xhigh_alarm,
                   dock_actions, dock_plus_placement
            FROM app_settings
            WHERE settings_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PixelDoneSettings();
        }

        var dockActions = reader.GetString(8)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => ParseEnum(value, DockAction.Sort))
            .Distinct()
            .Take(4)
            .ToArray();

        return new PixelDoneSettings(
            ParseEnum(reader.GetString(0), ThemeMode.System),
            ParseEnum(reader.GetString(1), LanguageMode.System),
            ParseEnum(reader.GetString(2), TodoSortMode.Priority),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            dockActions,
            ParseEnum(reader.GetString(9), DockPlusPlacement.Center));
    }

    public Task SaveSettingsAsync(
        PixelDoneSettings settings,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO app_settings (
                        settings_id, theme, language, sort_mode, show_ddl,
                        hide_completed, quick_delete, update_prompts,
                        enhanced_xhigh_alarm, dock_actions, dock_plus_placement,
                        updated_at_millis, sync_state)
                    VALUES (
                        1, $theme, $language, $sort, $ddl, $hide, $quick,
                        $updates, $xhigh, $dock, $placement, $updated, 'Dirty')
                    ON CONFLICT(settings_id) DO UPDATE SET
                        theme = excluded.theme,
                        language = excluded.language,
                        sort_mode = excluded.sort_mode,
                        show_ddl = excluded.show_ddl,
                        hide_completed = excluded.hide_completed,
                        quick_delete = excluded.quick_delete,
                        update_prompts = excluded.update_prompts,
                        enhanced_xhigh_alarm = excluded.enhanced_xhigh_alarm,
                        dock_actions = excluded.dock_actions,
                        dock_plus_placement = excluded.dock_plus_placement,
                        updated_at_millis = excluded.updated_at_millis,
                        sync_state = excluded.sync_state;
                    """;
                command.Parameters.AddWithValue("$theme", settings.Theme.ToString());
                command.Parameters.AddWithValue("$language", settings.Language.ToString());
                command.Parameters.AddWithValue("$sort", settings.SortMode.ToString());
                command.Parameters.AddWithValue("$ddl", settings.ShowDdl);
                command.Parameters.AddWithValue("$hide", settings.HideCompleted);
                command.Parameters.AddWithValue("$quick", settings.QuickDelete);
                command.Parameters.AddWithValue("$updates", settings.UpdatePrompts);
                command.Parameters.AddWithValue("$xhigh", settings.EnhancedXHighAlarm);
                command.Parameters.AddWithValue(
                    "$dock",
                    string.Join(',', settings.EffectiveDockActions));
                command.Parameters.AddWithValue(
                    "$placement",
                    settings.DockPlusPlacement.ToString());
                command.Parameters.AddWithValue(
                    "$updated",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

    public async Task<TodoAttachment?> GetAttachmentAsync(
        string todoId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, todo_local_id, local_path, remote_path, sha256,
                   mime_type, byte_size, updated_at_millis, sync_state,
                   remote_version, deleted_at_millis
            FROM todo_attachments
            WHERE todo_local_id = $todo;
            """;
        command.Parameters.AddWithValue("$todo", todoId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAttachment(reader) : null;
    }

    public Task UpsertAttachmentAsync(
        TodoAttachment attachment,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO todo_attachments (
                        local_id, todo_local_id, local_path, remote_path, sha256,
                        mime_type, byte_size, updated_at_millis, sync_state,
                        remote_version, deleted_at_millis)
                    VALUES (
                        $id, $todo, $local, $remote, $sha, $mime, $bytes,
                        $updated, $sync, $version, $deleted)
                    ON CONFLICT(todo_local_id) DO UPDATE SET
                        local_id = excluded.local_id,
                        local_path = excluded.local_path,
                        remote_path = excluded.remote_path,
                        sha256 = excluded.sha256,
                        mime_type = excluded.mime_type,
                        byte_size = excluded.byte_size,
                        updated_at_millis = excluded.updated_at_millis,
                        sync_state = excluded.sync_state,
                        remote_version = excluded.remote_version,
                        deleted_at_millis = excluded.deleted_at_millis;
                    """;
                AddAttachmentParameters(command, attachment);
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            cancellationToken);

    public Task RemoveAttachmentAsync(
        string todoId,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                var remoteVersion = await NullableLongAsync(
                    connection,
                    "SELECT remote_version FROM todo_attachments WHERE todo_local_id = $todo;",
                    cancellationToken,
                    parameters: [("$todo", todoId)]);
                if (remoteVersion is null)
                {
                    await ExecuteAsync(
                        connection,
                        "DELETE FROM todo_attachments WHERE todo_local_id = $todo;",
                        cancellationToken,
                        parameters: [("$todo", todoId)]);
                    return;
                }

                await ExecuteAsync(
                    connection,
                    """
                    UPDATE todo_attachments
                    SET local_path = NULL, sha256 = '', mime_type = '',
                        byte_size = 0, updated_at_millis = $now,
                        sync_state = 'Deleted', deleted_at_millis = $now
                    WHERE todo_local_id = $todo;
                    """,
                    cancellationToken,
                    parameters: [("$todo", todoId), ("$now", nowMillis)]);
            },
            cancellationToken);

    public async Task<IReadOnlyList<DueReminder>> ListDueRemindersAsync(
        long nowMillis,
        CancellationToken cancellationToken = default)
    {
        var items = new List<TodoItem>();
        await using var connection = await OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT local_id, checklist_local_id, title, priority, due_at_millis,
                       completed, created_at_millis, updated_at_millis, reminder_repeat,
                       image_local_name, image_remote_path, trashed_from_checklist_id,
                       trashed_from_checklist_name, trashed_at_millis, sort_index,
                       sync_state, remote_id, owner_user_id, remote_version,
                       last_synced_at_millis, last_sync_error
                FROM todo_items
                WHERE completed = 0
                  AND due_at_millis > 0
                  AND due_at_millis <= $now
                  AND checklist_local_id NOT IN ('trash', 'settings')
                ORDER BY due_at_millis;
                """;
            command.Parameters.AddWithValue("$now", nowMillis);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadTodo(reader));
            }
        }

        var due = new List<DueReminder>();
        foreach (var item in items)
        {
            var interval = item.ReminderRepeat switch
            {
                ReminderRepeat.Daily => TodoRules.DailyIntervalMillis,
                ReminderRepeat.Weekly => TodoRules.WeeklyIntervalMillis,
                _ => 0,
            };
            var occurrence = interval == 0
                ? item.DueAtMillis
                : item.DueAtMillis + ((nowMillis - item.DueAtMillis) / interval * interval);
            var delivered = await ScalarLongAsync(
                connection,
                """
                SELECT EXISTS(
                    SELECT 1 FROM reminder_deliveries
                    WHERE todo_local_id = $todo AND occurrence_at_millis = $occurrence);
                """,
                cancellationToken,
                parameters: [("$todo", item.Id), ("$occurrence", occurrence)]);
            if (delivered == 0)
            {
                due.Add(new DueReminder(item, occurrence));
            }
        }

        return due;
    }

    public Task MarkReminderDeliveredAsync(
        string todoId,
        long occurrenceAtMillis,
        long deliveredAtMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    INSERT OR IGNORE INTO reminder_deliveries (
                        todo_local_id, occurrence_at_millis, delivered_at_millis)
                    VALUES ($todo, $occurrence, $delivered);
                    UPDATE todo_items
                    SET due_at_millis = $occurrence +
                            CASE reminder_repeat
                                WHEN 'Daily' THEN 86400000
                                WHEN 'Weekly' THEN 604800000
                                ELSE 0
                            END,
                        updated_at_millis = $delivered,
                        sync_state = CASE
                            WHEN sync_state = 'LocalOnly' THEN 'LocalOnly'
                            ELSE 'Dirty'
                        END
                    WHERE local_id = $todo
                      AND reminder_repeat IN ('Daily', 'Weekly')
                      AND due_at_millis <= $occurrence;
                    DELETE FROM reminder_deliveries
                    WHERE delivered_at_millis < $oldest;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$todo", todoId),
                        ("$occurrence", occurrenceAtMillis),
                        ("$delivered", deliveredAtMillis),
                        ("$oldest", deliveredAtMillis - (90L * TodoRules.DailyIntervalMillis)),
                    ]),
            cancellationToken);

    public async Task<SyncMetadata> GetSyncMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT owner_user_id, cursor, schema_version, last_sync_at_millis, last_error
            FROM sync_metadata WHERE metadata_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SyncMetadata(null, 0, "3.2", null, null);
        }

        return new SyncMetadata(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public async Task<bool> PrepareCloudSessionAsync(
        string ownerUserId,
        long nowMillis,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetSyncMetadataAsync(cancellationToken);
        if (metadata.OwnerUserId == ownerUserId)
        {
            return false;
        }

        await WriteAsync(
            async connection =>
            {
                await ExecuteAsync(
                    connection,
                    """
                    DELETE FROM pending_mutations;
                    DELETE FROM reminder_deliveries;
                    DELETE FROM sync_tombstones;
                    DELETE FROM sync_conflicts;
                    DELETE FROM todo_attachments;
                    DELETE FROM todo_items;
                    DELETE FROM checklists;
                    """,
                    cancellationToken);
                await SeedAsync(connection, cancellationToken);
                await ExecuteAsync(
                    connection,
                    """
                    UPDATE sync_metadata
                    SET owner_user_id = $owner, cursor = 0, schema_version = '3.2',
                        last_sync_at_millis = NULL, last_error = NULL
                    WHERE metadata_id = 1;
                    """,
                    cancellationToken,
                    parameters: [("$owner", ownerUserId)]);
            },
            cancellationToken);
        return true;
    }

    public Task FinishCloudRestoreAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                var normalCount = await ScalarLongAsync(
                    connection,
                    """
                    SELECT COUNT(*) FROM checklists
                    WHERE local_id NOT IN ('trash', 'settings')
                      AND deleted_at_millis IS NULL;
                    """,
                    cancellationToken);
                if (normalCount > 1)
                {
                    normalCount -= await ExecuteAsync(
                        connection,
                        """
                        DELETE FROM checklists
                        WHERE local_id = 'main'
                          AND sync_state = 'LocalOnly'
                          AND remote_id IS NULL;
                        """,
                        cancellationToken);
                }

                if (normalCount == 0)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await ExecuteAsync(
                        connection,
                        """
                        INSERT INTO checklists(
                            local_id, sort_index, name, created_at_millis,
                            updated_at_millis, sync_state)
                        VALUES ('main', 0, 'MAIN', $now, $now, 'LocalOnly');
                        """,
                        cancellationToken,
                        parameters: [("$now", now)]);
                }
            },
            cancellationToken);

    public Task SaveSyncSuccessAsync(
        string ownerUserId,
        long cursor,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    UPDATE sync_metadata
                    SET owner_user_id = $owner,
                        cursor = MAX(cursor, $cursor),
                        schema_version = '3.2',
                        last_sync_at_millis = $now,
                        last_error = NULL
                    WHERE metadata_id = 1;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$owner", ownerUserId),
                        ("$cursor", cursor),
                        ("$now", nowMillis),
                    ]),
            cancellationToken);

    public Task SaveSyncErrorAsync(
        string message,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    "UPDATE sync_metadata SET last_error = $error WHERE metadata_id = 1;",
                    cancellationToken,
                    parameters: [("$error", message)]),
            cancellationToken);

    public async Task<IReadOnlyList<TodoChecklist>> ListDirtyChecklistsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<TodoChecklist>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, name, sort_index, created_at_millis, updated_at_millis,
                   sync_state, remote_id, owner_user_id, remote_version, deleted_at_millis
            FROM checklists
            WHERE deleted_at_millis IS NULL
              AND local_id NOT IN ('trash', 'settings')
              AND sync_state IN ('LocalOnly', 'Dirty')
            ORDER BY sort_index, created_at_millis;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadChecklist(reader));
        }

        return result;
    }

    public async Task<IReadOnlyList<TodoItem>> ListDirtyTodosAsync(
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
                   trashed_from_checklist_name, trashed_at_millis, sort_index,
                   sync_state, remote_id, owner_user_id, remote_version,
                   last_synced_at_millis, last_sync_error
            FROM todo_items
            WHERE sync_state IN ('LocalOnly', 'Dirty')
            ORDER BY checklist_local_id, sort_index, created_at_millis;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadTodo(reader));
        }

        return result;
    }

    public async Task<IReadOnlyList<TodoAttachment>> ListDirtyAttachmentsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<TodoAttachment>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT local_id, todo_local_id, local_path, remote_path, sha256,
                   mime_type, byte_size, updated_at_millis, sync_state,
                   remote_version, deleted_at_millis
            FROM todo_attachments
            WHERE sync_state IN ('LocalOnly', 'Dirty', 'Deleted')
            ORDER BY updated_at_millis;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadAttachment(reader));
        }

        return result;
    }

    public async Task<SettingsSyncRecord> GetSettingsForSyncAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT updated_at_millis, sync_state, remote_version
            FROM app_settings WHERE settings_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new SettingsSyncRecord(settings, 0, SyncState.LocalOnly, null);
        }

        return new SettingsSyncRecord(
            settings,
            reader.GetInt64(0),
            ParseEnum(reader.GetString(1), SyncState.LocalOnly),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    public async Task<IReadOnlyList<SyncTombstone>> ListDirtyTombstonesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<SyncTombstone>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT tombstone_id, record_type, local_id, deleted_at_millis,
                   remote_version, sync_state
            FROM sync_tombstones
            WHERE sync_state = 'Dirty'
            ORDER BY deleted_at_millis;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new SyncTombstone(
                    reader.GetString(0),
                    ParseEnum(reader.GetString(1), SyncRecordType.Todo),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    ParseEnum(reader.GetString(5), SyncState.Dirty)));
        }

        return result;
    }

    public Task ApplyRemoteSettingsAsync(
        LanguageMode language,
        long updatedAtMillis,
        long? remoteVersion,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    UPDATE app_settings
                    SET language = $language, updated_at_millis = $updated,
                        sync_state = 'Clean', remote_version = $version,
                        last_synced_at_millis = $now, last_sync_error = NULL
                    WHERE settings_id = 1;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$language", language.ToString()),
                        ("$updated", updatedAtMillis),
                        ("$version", remoteVersion),
                        ("$now", nowMillis),
                    ]),
            cancellationToken);

    public Task ApplyRemoteTombstoneAsync(
        SyncRecordType recordType,
        string localId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                switch (recordType)
                {
                    case SyncRecordType.Checklist:
                        await ExecuteAsync(
                            connection,
                            """
                            DELETE FROM todo_items WHERE checklist_local_id = $id;
                            DELETE FROM checklists WHERE local_id = $id;
                            """,
                            cancellationToken,
                            parameters: [("$id", localId)]);
                        break;
                    case SyncRecordType.Todo:
                        await ExecuteAsync(
                            connection,
                            "DELETE FROM todo_items WHERE local_id = $id;",
                            cancellationToken,
                            parameters: [("$id", localId)]);
                        break;
                    case SyncRecordType.Attachment:
                        await ExecuteAsync(
                            connection,
                            """
                            DELETE FROM todo_attachments
                            WHERE todo_local_id = $id OR local_id = $id;
                            """,
                            cancellationToken,
                            parameters: [("$id", localId)]);
                        break;
                    case SyncRecordType.Settings:
                        break;
                }
            },
            cancellationToken);

    public Task AcknowledgeTombstoneAsync(
        SyncRecordType recordType,
        string localId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    DELETE FROM sync_tombstones
                    WHERE record_type = $type AND local_id = $id;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$type", recordType.ToString()),
                        ("$id", localId),
                    ]),
            cancellationToken);

    public Task RecordConflictAsync(
        SyncConflict conflict,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO sync_conflicts(
                        conflict_id, record_type, local_id, fields_json,
                        local_json, cloud_json, created_at_millis, resolved_at_millis)
                    VALUES(
                        $id, $type, $local, $fields, $localJson,
                        $cloudJson, $created, $resolved)
                    ON CONFLICT(conflict_id) DO UPDATE SET
                        fields_json = excluded.fields_json,
                        local_json = excluded.local_json,
                        cloud_json = excluded.cloud_json,
                        created_at_millis = excluded.created_at_millis,
                        resolved_at_millis = NULL;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$id", conflict.Id),
                        ("$type", conflict.RecordType.ToString()),
                        ("$local", conflict.LocalId),
                        ("$fields", conflict.FieldsJson),
                        ("$localJson", conflict.LocalJson),
                        ("$cloudJson", conflict.CloudJson),
                        ("$created", conflict.CreatedAtMillis),
                        ("$resolved", conflict.ResolvedAtMillis),
                    ]);
                var (table, key) = conflict.RecordType switch
                {
                    SyncRecordType.Checklist => ("checklists", "local_id"),
                    SyncRecordType.Todo => ("todo_items", "local_id"),
                    SyncRecordType.Attachment => ("todo_attachments", "todo_local_id"),
                    _ => ("app_settings", "settings_id"),
                };
                var predicate = conflict.RecordType == SyncRecordType.Settings
                    ? $"{key} = 1"
                    : $"{key} = $local";
                await ExecuteAsync(
                    connection,
                    $"UPDATE {table} SET sync_state = 'Conflict' WHERE {predicate};",
                    cancellationToken,
                    parameters: [("$local", conflict.LocalId)]);
            },
            cancellationToken);

    public async Task<IReadOnlyList<SyncConflict>> ListOpenConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<SyncConflict>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT conflict_id, record_type, local_id, fields_json,
                   local_json, cloud_json, created_at_millis, resolved_at_millis
            FROM sync_conflicts
            WHERE resolved_at_millis IS NULL
            ORDER BY created_at_millis;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(
                new SyncConflict(
                    reader.GetString(0),
                    ParseEnum(reader.GetString(1), SyncRecordType.Todo),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        }

        return result;
    }

    public Task ResolveConflictKeepLocalAsync(
        string conflictId,
        long? remoteVersion,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            async connection =>
            {
                string? rawType;
                string? localId;
                await using (var query = connection.CreateCommand())
                {
                    query.CommandText =
                        """
                        SELECT record_type, local_id FROM sync_conflicts
                        WHERE conflict_id = $id AND resolved_at_millis IS NULL;
                        """;
                    query.Parameters.AddWithValue("$id", conflictId);
                    await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return;
                    }

                    rawType = reader.GetString(0);
                    localId = reader.GetString(1);
                }

                var recordType = ParseEnum(rawType, SyncRecordType.Todo);
                var (table, key) = recordType switch
                {
                    SyncRecordType.Checklist => ("checklists", "local_id"),
                    SyncRecordType.Todo => ("todo_items", "local_id"),
                    SyncRecordType.Attachment => ("todo_attachments", "todo_local_id"),
                    _ => ("app_settings", "settings_id"),
                };
                var predicate = recordType == SyncRecordType.Settings
                    ? $"{key} = 1"
                    : $"{key} = $local";
                await ExecuteAsync(
                    connection,
                    $"""
                     UPDATE {table}
                     SET sync_state = 'Dirty', remote_version = $version
                     WHERE {predicate};
                     UPDATE sync_conflicts
                     SET resolved_at_millis = $now WHERE conflict_id = $id;
                     """,
                    cancellationToken,
                    parameters:
                    [
                        ("$local", localId),
                        ("$version", remoteVersion),
                        ("$now", nowMillis),
                        ("$id", conflictId),
                    ]);
            },
            cancellationToken);

    public Task MarkConflictResolvedAsync(
        string conflictId,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    UPDATE sync_conflicts SET resolved_at_millis = $now
                    WHERE conflict_id = $id AND resolved_at_millis IS NULL;
                    """,
                    cancellationToken,
                    parameters: [("$now", nowMillis), ("$id", conflictId)]),
            cancellationToken);

    public Task SavePendingMutationAsync(
        string mutationId,
        string payloadJson,
        long nowMillis,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    INSERT INTO pending_mutations(
                        mutation_id, record_type, local_id, payload_json, created_at_millis)
                    VALUES($id, 'Batch', 'all', $payload, $now)
                    ON CONFLICT(mutation_id) DO NOTHING;
                    """,
                    cancellationToken,
                    parameters:
                    [
                        ("$id", mutationId),
                        ("$payload", payloadJson),
                        ("$now", nowMillis),
                    ]),
            cancellationToken);

    public async Task<PendingMutation?> GetPendingMutationAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT mutation_id, payload_json, created_at_millis,
                   attempt_count, last_error
            FROM pending_mutations
            ORDER BY created_at_millis LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PendingMutation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4))
            : null;
    }

    public Task FailPendingMutationAsync(
        string mutationId,
        string error,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    """
                    UPDATE pending_mutations
                    SET attempt_count = attempt_count + 1, last_error = $error
                    WHERE mutation_id = $id;
                    """,
                    cancellationToken,
                    parameters: [("$id", mutationId), ("$error", error)]),
            cancellationToken);

    public Task ClearPendingMutationAsync(
        string mutationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            connection =>
                ExecuteAsync(
                    connection,
                    "DELETE FROM pending_mutations WHERE mutation_id = $id;",
                    cancellationToken,
                    parameters: [("$id", mutationId)]),
            cancellationToken);

    private async Task SeedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var checklist in new[]
                 {
                     new TodoChecklist(
                         PixelDoneChecklists.MainId,
                         PixelDoneChecklists.MainName,
                         0,
                         now,
                         now),
                     new TodoChecklist(
                         PixelDoneChecklists.TrashId,
                         PixelDoneChecklists.TrashName,
                         int.MaxValue - 1,
                         now,
                         now),
                     new TodoChecklist(
                         PixelDoneChecklists.SettingsId,
                         PixelDoneChecklists.SettingsName,
                         int.MaxValue,
                         now,
                         now),
                 })
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO checklists (
                    local_id, sort_index, name, created_at_millis,
                    updated_at_millis, sync_state)
                VALUES ($id, $sort, $name, $created, $updated, 'LocalOnly');
                """;
            command.Parameters.AddWithValue("$id", checklist.Id);
            command.Parameters.AddWithValue("$sort", checklist.SortIndex);
            command.Parameters.AddWithValue("$name", checklist.Name);
            command.Parameters.AddWithValue("$created", checklist.CreatedAtMillis);
            command.Parameters.AddWithValue("$updated", checklist.UpdatedAtMillis);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(
            connection,
            """
            INSERT OR IGNORE INTO app_settings (
                settings_id, theme, language, sort_mode, show_ddl,
                hide_completed, quick_delete, update_prompts,
                enhanced_xhigh_alarm, dock_actions, dock_plus_placement,
                updated_at_millis, sync_state)
            VALUES (
                1, 'System', 'System', 'Priority', 1, 0, 0, 1, 0,
                'Sort,Ddl', 'Center', $now, 'LocalOnly');

            INSERT OR IGNORE INTO sync_metadata(
                metadata_id, cursor, schema_version)
            VALUES (1, 0, '3.2');
            """,
            cancellationToken,
            parameters: [("$now", now)]);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(
            connection,
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """,
            cancellationToken);
        return connection;
    }

    private async Task WriteAsync(
        Func<SqliteConnection, Task> action,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await action(connection);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WriteTransactionAsync(
        Func<SqliteConnection, SqliteTransaction, Task> action,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await action(connection, (SqliteTransaction)transaction);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long?> NullableLongAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task<string?> ScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static async Task InsertTombstoneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncRecordType recordType,
        string localId,
        long nowMillis,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            """
            INSERT INTO sync_tombstones (
                tombstone_id, record_type, local_id, deleted_at_millis,
                sync_state)
            VALUES ($id, $type, $local, $deleted, 'Dirty')
            ON CONFLICT(tombstone_id) DO UPDATE SET
                deleted_at_millis = excluded.deleted_at_millis,
                sync_state = 'Dirty';
            """,
            cancellationToken,
            transaction,
            ("$id", $"{recordType}:{localId}"),
            ("$type", recordType.ToString()),
            ("$local", localId),
            ("$deleted", nowMillis));
    }

    private static TodoChecklist ReadChecklist(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            ParseEnum(reader.GetString(5), SyncState.LocalOnly),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9));

    private static TodoItem ReadTodo(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseEnum(reader.GetString(3), TodoPriority.Medium),
            reader.GetInt64(4),
            reader.GetBoolean(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            ParseEnum(reader.GetString(8), ReminderRepeat.None),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt64(13),
            reader.GetInt32(14),
            ParseEnum(reader.GetString(15), SyncState.LocalOnly),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetInt64(18),
            reader.IsDBNull(19) ? null : reader.GetInt64(19),
            reader.IsDBNull(20) ? null : reader.GetString(20));

    private static TodoAttachment ReadAttachment(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            ParseEnum(reader.GetString(8), SyncState.LocalOnly),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10));

    private static void AddChecklistParameters(
        SqliteCommand command,
        TodoChecklist checklist)
    {
        command.Parameters.AddWithValue("$id", checklist.Id);
        command.Parameters.AddWithValue("$sort", checklist.SortIndex);
        command.Parameters.AddWithValue("$name", checklist.Name);
        command.Parameters.AddWithValue("$created", checklist.CreatedAtMillis);
        command.Parameters.AddWithValue("$updated", checklist.UpdatedAtMillis);
        command.Parameters.AddWithValue("$sync", checklist.SyncState.ToString());
        command.Parameters.AddWithValue("$remote", Db(checklist.RemoteId));
        command.Parameters.AddWithValue("$owner", Db(checklist.OwnerUserId));
        command.Parameters.AddWithValue("$version", Db(checklist.RemoteVersion));
        command.Parameters.AddWithValue("$deleted", Db(checklist.DeletedAtMillis));
    }

    private static void AddTodoParameters(SqliteCommand command, TodoItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$checklist", item.ChecklistId);
        command.Parameters.AddWithValue("$sort", item.SortIndex);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$priority", item.Priority.ToString());
        command.Parameters.AddWithValue("$due", item.DueAtMillis);
        command.Parameters.AddWithValue("$completed", item.Completed);
        command.Parameters.AddWithValue("$created", item.CreatedAtMillis);
        command.Parameters.AddWithValue("$updated", item.UpdatedAtMillis);
        command.Parameters.AddWithValue("$repeat", item.ReminderRepeat.ToString());
        command.Parameters.AddWithValue("$imageLocal", Db(item.ImageLocalName));
        command.Parameters.AddWithValue("$imageRemote", Db(item.ImageRemotePath));
        command.Parameters.AddWithValue("$trashedId", Db(item.TrashedFromChecklistId));
        command.Parameters.AddWithValue("$trashedName", Db(item.TrashedFromChecklistName));
        command.Parameters.AddWithValue("$trashedAt", Db(item.TrashedAtMillis));
        command.Parameters.AddWithValue("$sync", item.SyncState.ToString());
        command.Parameters.AddWithValue("$remote", Db(item.RemoteId));
        command.Parameters.AddWithValue("$owner", Db(item.OwnerUserId));
        command.Parameters.AddWithValue("$version", Db(item.RemoteVersion));
        command.Parameters.AddWithValue("$lastSynced", Db(item.LastSyncedAtMillis));
        command.Parameters.AddWithValue("$lastError", Db(item.LastSyncError));
    }

    private static void AddAttachmentParameters(
        SqliteCommand command,
        TodoAttachment attachment)
    {
        command.Parameters.AddWithValue("$id", attachment.Id);
        command.Parameters.AddWithValue("$todo", attachment.TodoId);
        command.Parameters.AddWithValue("$local", Db(attachment.LocalPath));
        command.Parameters.AddWithValue("$remote", Db(attachment.RemotePath));
        command.Parameters.AddWithValue("$sha", attachment.Sha256);
        command.Parameters.AddWithValue("$mime", attachment.MimeType);
        command.Parameters.AddWithValue("$bytes", attachment.ByteSize);
        command.Parameters.AddWithValue("$updated", attachment.UpdatedAtMillis);
        command.Parameters.AddWithValue("$sync", attachment.SyncState.ToString());
        command.Parameters.AddWithValue("$version", Db(attachment.RemoteVersion));
        command.Parameters.AddWithValue("$deleted", Db(attachment.DeletedAtMillis));
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name);
            """;
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static T ParseEnum<T>(string value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
}
