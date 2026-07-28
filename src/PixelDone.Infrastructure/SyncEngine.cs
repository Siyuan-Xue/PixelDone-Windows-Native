using System.Security.Cryptography;
using System.Text.Json;
using PixelDone.Core;

namespace PixelDone.Infrastructure;

public enum ConflictChoice
{
    KeepLocal,
    KeepCloud,
}

public sealed class SyncEngine(
    SupabaseClient cloud,
    ITodoRepository todos,
    ISyncRepository syncRepository,
    string attachmentCacheDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public async Task<SyncSummary> SyncAsync(
        AuthSession session,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var metadata = await syncRepository.GetSyncMetadataAsync(cancellationToken);
        var firstCloudRestore = metadata.OwnerUserId != session.UserId;
        RemoteChangeBatch pulled;
        try
        {
            pulled = await cloud.RpcAsync<RemoteChangeBatch>(
                session,
                "pixeldone_pull_changes",
                new Dictionary<string, object?>
                {
                    ["p_since_version"] = firstCloudRestore ? 0 : metadata.Cursor,
                    ["p_client_schema_version"] = SupabaseClient.ExpectedSchema,
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            await syncRepository.SaveSyncErrorAsync(exception.Message, cancellationToken);
            throw;
        }

        SupabaseClient.RequireSchema(pulled.SchemaVersion);
        if (firstCloudRestore)
        {
            await syncRepository.PrepareCloudSessionAsync(
                session.UserId,
                now,
                cancellationToken);
        }

        await ApplyPullAsync(session, pulled, firstCloudRestore, cancellationToken);
        if (firstCloudRestore)
        {
            await syncRepository.FinishCloudRestoreAsync(cancellationToken);
        }

        await syncRepository.SaveSyncSuccessAsync(
            session.UserId,
            pulled.ServerVersion,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken);
        var cleanedPaths = await DeleteQueuedImagesAsync(
            session,
            pulled.EffectiveImageCleanupPaths,
            cancellationToken);

        MutationPayload payload;
        var pending = await syncRepository.GetPendingMutationAsync(cancellationToken);
        if (pending is not null)
        {
            payload =
                JsonSerializer.Deserialize<MutationPayload>(pending.PayloadJson, JsonOptions) ??
                throw new InvalidOperationException("The pending sync mutation is invalid.");
        }
        else
        {
            payload = await BuildMutationAsync(
                session,
                cleanedPaths,
                cancellationToken);
            if (!payload.IsEmpty)
            {
                await syncRepository.SavePendingMutationAsync(
                    payload.MutationUuid,
                    JsonSerializer.Serialize(payload, JsonOptions),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cancellationToken);
            }
        }

        var pulledCount =
            pulled.EffectiveChecklists.Count +
            pulled.EffectiveItems.Count +
            pulled.EffectiveAttachments.Count +
            pulled.EffectiveTombstones.Count +
            (pulled.Settings is null ? 0 : 1);
        if (payload.IsEmpty)
        {
            return new SyncSummary(
                firstCloudRestore,
                pulledCount,
                0,
                (await syncRepository.ListOpenConflictsAsync(cancellationToken)).Count,
                pulled.ServerVersion);
        }

        RemotePushResult pushed;
        try
        {
            pushed = await cloud.RpcAsync<RemotePushResult>(
                session,
                "pixeldone_apply_mutation",
                payload.RpcBody(),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await syncRepository.FailPendingMutationAsync(
                payload.MutationUuid,
                exception.Message,
                cancellationToken);
            await syncRepository.SaveSyncErrorAsync(exception.Message, cancellationToken);
            throw;
        }

        SupabaseClient.RequireSchema(pushed.SchemaVersion);
        await ApplyPushAsync(session, pushed, cancellationToken);
        await syncRepository.ClearPendingMutationAsync(
            payload.MutationUuid,
            cancellationToken);
        await syncRepository.SaveSyncSuccessAsync(
            session.UserId,
            pushed.ServerVersion,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken);
        return new SyncSummary(
            firstCloudRestore,
            pulledCount,
            payload.RecordCount,
            (await syncRepository.ListOpenConflictsAsync(cancellationToken)).Count,
            pushed.ServerVersion);
    }

    public Task<IReadOnlyList<SyncConflict>> ListConflictsAsync(
        CancellationToken cancellationToken = default) =>
        syncRepository.ListOpenConflictsAsync(cancellationToken);

    public async Task ResolveConflictAsync(
        AuthSession session,
        string conflictId,
        ConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        var conflict = (await syncRepository.ListOpenConflictsAsync(cancellationToken))
            .FirstOrDefault(value => value.Id == conflictId) ??
            throw new InvalidOperationException($"Conflict {conflictId} was not found.");
        var remoteVersion = RemoteVersion(conflict.CloudJson);
        if (choice == ConflictChoice.KeepLocal)
        {
            await syncRepository.ResolveConflictKeepLocalAsync(
                conflictId,
                remoteVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken);
            return;
        }

        switch (conflict.RecordType)
        {
            case SyncRecordType.Checklist:
                await todos.UpsertChecklistAsync(
                    ToLocal(
                        JsonSerializer.Deserialize<RemoteChecklist>(
                            conflict.CloudJson,
                            JsonOptions) ??
                        throw new InvalidOperationException("Cloud checklist is unavailable.")),
                    cancellationToken);
                break;
            case SyncRecordType.Todo:
                await todos.UpsertAsync(
                    ToLocal(
                        JsonSerializer.Deserialize<RemoteTodo>(
                            conflict.CloudJson,
                            JsonOptions) ??
                        throw new InvalidOperationException("Cloud task is unavailable.")),
                    cancellationToken);
                break;
            case SyncRecordType.Attachment:
                await ApplyRemoteAttachmentAsync(
                    session,
                    JsonSerializer.Deserialize<RemoteAttachment>(
                        conflict.CloudJson,
                        JsonOptions) ??
                    throw new InvalidOperationException("Cloud attachment is unavailable."),
                    cancellationToken);
                break;
            case SyncRecordType.Settings:
                var remote = JsonSerializer.Deserialize<RemoteSettings>(
                                 conflict.CloudJson,
                                 JsonOptions) ??
                             throw new InvalidOperationException("Cloud settings are unavailable.");
                await syncRepository.ApplyRemoteSettingsAsync(
                    ParseLanguage(remote.LanguageMode),
                    remote.UpdatedAtMillis,
                    remote.RemoteVersion,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cancellationToken);
                break;
        }

        await syncRepository.MarkConflictResolvedAsync(
            conflictId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cancellationToken);
    }

    private async Task ApplyPullAsync(
        AuthSession session,
        RemoteChangeBatch batch,
        bool forceCloud,
        CancellationToken cancellationToken)
    {
        foreach (var tombstone in batch.EffectiveTombstones)
        {
            var type = ParseRecordType(tombstone.RecordType);
            await syncRepository.ApplyRemoteTombstoneAsync(
                type,
                tombstone.LocalId,
                cancellationToken);
            await syncRepository.AcknowledgeTombstoneAsync(
                type,
                tombstone.LocalId,
                cancellationToken);
        }

        var localChecklists = await todos.ListChecklistsAsync(cancellationToken);
        foreach (var remote in batch.EffectiveChecklists)
        {
            var local = localChecklists.FirstOrDefault(value => value.Id == remote.LocalId);
            if (!forceCloud && local is not null && IsLocallyChanged(local.SyncState))
            {
                await RecordPullConflictAsync(
                    SyncRecordType.Checklist,
                    remote.LocalId,
                    local,
                    remote,
                    cancellationToken);
            }
            else
            {
                await todos.UpsertChecklistAsync(ToLocal(remote), cancellationToken);
            }
        }

        foreach (var remote in batch.EffectiveItems)
        {
            var local = await todos.GetAsync(remote.LocalId, cancellationToken);
            if (!forceCloud && local is not null && IsLocallyChanged(local.SyncState))
            {
                await RecordPullConflictAsync(
                    SyncRecordType.Todo,
                    remote.LocalId,
                    local,
                    remote,
                    cancellationToken);
            }
            else
            {
                await todos.UpsertAsync(ToLocal(remote), cancellationToken);
            }
        }

        foreach (var remote in batch.EffectiveAttachments)
        {
            var local = await todos.GetAttachmentAsync(
                remote.TodoLocalId,
                cancellationToken);
            if (!forceCloud && local is not null && IsLocallyChanged(local.SyncState))
            {
                await RecordPullConflictAsync(
                    SyncRecordType.Attachment,
                    remote.TodoLocalId,
                    local,
                    remote,
                    cancellationToken);
            }
            else
            {
                await ApplyRemoteAttachmentAsync(session, remote, cancellationToken);
            }
        }

        if (batch.Settings is { } settings)
        {
            var local = await syncRepository.GetSettingsForSyncAsync(cancellationToken);
            if (!forceCloud && IsLocallyChanged(local.SyncState))
            {
                await RecordSettingsConflictAsync(local, settings, cancellationToken);
            }
            else
            {
                await syncRepository.ApplyRemoteSettingsAsync(
                    ParseLanguage(settings.LanguageMode),
                    settings.UpdatedAtMillis,
                    settings.RemoteVersion,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cancellationToken);
            }
        }
    }

    private async Task ApplyPushAsync(
        AuthSession session,
        RemotePushResult result,
        CancellationToken cancellationToken)
    {
        foreach (var checklist in result.EffectiveAccepted.EffectiveChecklists)
        {
            await todos.UpsertChecklistAsync(ToLocal(checklist), cancellationToken);
        }

        foreach (var item in result.EffectiveAccepted.EffectiveItems)
        {
            await todos.UpsertAsync(ToLocal(item), cancellationToken);
        }

        foreach (var attachment in result.EffectiveAccepted.EffectiveAttachments)
        {
            await ApplyRemoteAttachmentAsync(session, attachment, cancellationToken);
        }

        if (result.Settings is { } settings)
        {
            await syncRepository.ApplyRemoteSettingsAsync(
                ParseLanguage(settings.LanguageMode),
                settings.UpdatedAtMillis,
                settings.RemoteVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken);
        }

        foreach (var tombstone in result.EffectiveTombstones)
        {
            await syncRepository.AcknowledgeTombstoneAsync(
                ParseRecordType(tombstone.RecordType),
                tombstone.LocalId,
                cancellationToken);
        }

        foreach (var conflict in result.EffectiveConflicts)
        {
            await RecordServerConflictAsync(conflict, cancellationToken);
        }

        _ = await DeleteQueuedImagesAsync(
            session,
            result.EffectiveImageCleanupPaths,
            cancellationToken);
    }

    private async Task<MutationPayload> BuildMutationAsync(
        AuthSession session,
        IReadOnlyList<string> cleanedImagePaths,
        CancellationToken cancellationToken)
    {
        var checklists = (await syncRepository.ListDirtyChecklistsAsync(cancellationToken))
            .Select(value => ToRemote(value, session.UserId))
            .ToArray();
        var items = (await syncRepository.ListDirtyTodosAsync(cancellationToken))
            .Select(value => ToRemote(value, session.UserId))
            .ToArray();
        var attachments = new List<RemoteAttachment>();
        foreach (var local in await syncRepository.ListDirtyAttachmentsAsync(cancellationToken))
        {
            if (local.DeletedAtMillis is not null)
            {
                attachments.Add(ToRemote(local, session.UserId));
                continue;
            }

            if (string.IsNullOrWhiteSpace(local.LocalPath))
            {
                throw new InvalidOperationException(
                    $"Attachment {local.Id} has no readable local file.");
            }

            var bytes = await File.ReadAllBytesAsync(local.LocalPath, cancellationToken);
            var image = InspectImage(bytes);
            var objectPath =
                $"{session.UserId}/{local.TodoId}/{local.Id}.{image.Extension}";
            await cloud.UploadTodoImageAsync(
                session,
                objectPath,
                image.ContentType,
                bytes,
                cancellationToken);
            var uploaded = local with
            {
                RemotePath = objectPath,
                Sha256 = image.Sha256,
                MimeType = image.ContentType,
                ByteSize = bytes.LongLength,
            };
            await todos.UpsertAttachmentAsync(uploaded, cancellationToken);
            attachments.Add(ToRemote(uploaded, session.UserId));
        }

        var localSettings = await syncRepository.GetSettingsForSyncAsync(cancellationToken);
        var settings = IsLocallyChanged(localSettings.SyncState)
            ? new RemoteSettings(
                session.UserId,
                LanguageValue(localSettings.Settings.Language),
                localSettings.UpdatedAtMillis,
                localSettings.RemoteVersion)
            : null;
        var tombstones = (await syncRepository.ListDirtyTombstonesAsync(cancellationToken))
            .Select(
                value => new RemoteTombstone(
                    session.UserId,
                    RecordTypeValue(value.RecordType),
                    value.LocalId,
                    value.DeletedAtMillis,
                    value.RemoteVersion))
            .ToArray();
        return new MutationPayload(
            Guid.NewGuid().ToString(),
            checklists,
            items,
            attachments,
            settings,
            tombstones,
            cleanedImagePaths);
    }

    private async Task ApplyRemoteAttachmentAsync(
        AuthSession session,
        RemoteAttachment remote,
        CancellationToken cancellationToken)
    {
        if (remote.DeletedAtMillis is not null)
        {
            await syncRepository.ApplyRemoteTombstoneAsync(
                SyncRecordType.Attachment,
                remote.TodoLocalId,
                cancellationToken);
            return;
        }

        if (remote.AttachmentId is null ||
            remote.ObjectPath is null ||
            remote.ContentSha256 is null ||
            remote.ContentType is null)
        {
            throw new InvalidOperationException("Cloud attachment metadata is incomplete.");
        }

        var bytes = await cloud.DownloadTodoImageAsync(
            session,
            remote.ObjectPath,
            cancellationToken);
        var image = InspectImage(bytes);
        if (!string.Equals(
                image.Sha256,
                remote.ContentSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cloud attachment hash does not match.");
        }

        Directory.CreateDirectory(attachmentCacheDirectory);
        var fileName =
            $"{SafeId(remote.TodoLocalId)}-{SafeId(remote.AttachmentId)}-" +
            $"{image.Sha256[..16]}.{image.Extension}";
        var path = Path.Combine(attachmentCacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        await todos.UpsertAttachmentAsync(
            new TodoAttachment(
                remote.AttachmentId,
                remote.TodoLocalId,
                path,
                remote.ObjectPath,
                remote.ContentSha256,
                remote.ContentType,
                remote.ByteSize ?? bytes.LongLength,
                remote.UpdatedAtMillis,
                SyncState.Clean,
                remote.RemoteVersion),
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> DeleteQueuedImagesAsync(
        AuthSession session,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var deleted = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                await cloud.DeleteTodoImageAsync(session, path, cancellationToken);
                deleted.Add(path);
            }
            catch (HttpRequestException)
            {
            }
        }

        return deleted;
    }

    private Task RecordPullConflictAsync<TLocal, TRemote>(
        SyncRecordType recordType,
        string localId,
        TLocal local,
        TRemote remote,
        CancellationToken cancellationToken) =>
        syncRepository.RecordConflictAsync(
            new SyncConflict(
                $"{recordType}:{localId}",
                recordType,
                localId,
                "[\"all\"]",
                JsonSerializer.Serialize(local, JsonOptions),
                JsonSerializer.Serialize(remote, JsonOptions),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);

    private Task RecordSettingsConflictAsync(
        SettingsSyncRecord local,
        RemoteSettings remote,
        CancellationToken cancellationToken) =>
        syncRepository.RecordConflictAsync(
            new SyncConflict(
                "Settings:settings",
                SyncRecordType.Settings,
                "settings",
                "[\"language_mode\"]",
                JsonSerializer.Serialize(
                    new
                    {
                        language_mode = LanguageValue(local.Settings.Language),
                        updated_at_millis = local.UpdatedAtMillis,
                        remote_version = local.RemoteVersion,
                    },
                    JsonOptions),
                JsonSerializer.Serialize(remote, JsonOptions),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);

    private async Task RecordServerConflictAsync(
        RemoteConflict remote,
        CancellationToken cancellationToken)
    {
        var recordType = ParseRecordType(remote.RecordType);
        var existing = (await syncRepository.ListOpenConflictsAsync(cancellationToken))
            .Any(value => value.RecordType == recordType && value.LocalId == remote.LocalId);
        if (existing)
        {
            return;
        }

        object? local = recordType switch
        {
            SyncRecordType.Checklist =>
                (await todos.ListChecklistsAsync(cancellationToken))
                .FirstOrDefault(value => value.Id == remote.LocalId),
            SyncRecordType.Todo => await todos.GetAsync(remote.LocalId, cancellationToken),
            SyncRecordType.Attachment =>
                await todos.GetAttachmentAsync(remote.LocalId, cancellationToken),
            _ => await syncRepository.GetSettingsForSyncAsync(cancellationToken),
        };
        await syncRepository.RecordConflictAsync(
            new SyncConflict(
                $"{recordType}:{remote.LocalId}",
                recordType,
                remote.LocalId,
                JsonSerializer.Serialize(new[] { remote.Message }, JsonOptions),
                JsonSerializer.Serialize(local, JsonOptions),
                "{}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            cancellationToken);
    }

    private static TodoChecklist ToLocal(RemoteChecklist value) =>
        new(
            value.LocalId,
            value.Name,
            value.SortIndex,
            value.CreatedAtMillis,
            value.UpdatedAtMillis,
            SyncState.Clean,
            value.Id,
            value.OwnerUserId,
            value.RemoteVersion);

    private static RemoteChecklist ToRemote(TodoChecklist value, string owner) =>
        new(
            value.Id,
            value.RemoteId,
            owner,
            value.SortIndex,
            value.Name,
            value.CreatedAtMillis,
            value.UpdatedAtMillis,
            value.RemoteVersion);

    private static TodoItem ToLocal(RemoteTodo value) =>
        new(
            value.LocalId,
            value.ChecklistLocalId,
            value.Title,
            ParsePriority(value.Priority),
            value.DueAtMillis,
            value.Completed,
            value.CreatedAtMillis,
            value.UpdatedAtMillis,
            ParseRepeat(value.ReminderRepeat),
            TrashedFromChecklistId: value.TrashedFromChecklistId,
            TrashedFromChecklistName: value.TrashedFromChecklistName,
            TrashedAtMillis: value.TrashedAtMillis,
            SortIndex: value.SortIndex,
            SyncState: SyncState.Clean,
            RemoteId: value.Id,
            OwnerUserId: value.OwnerUserId,
            RemoteVersion: value.RemoteVersion,
            LastSyncedAtMillis: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static RemoteTodo ToRemote(TodoItem value, string owner) =>
        new(
            value.Id,
            value.RemoteId,
            owner,
            value.ChecklistId,
            value.SortIndex,
            value.Title,
            PriorityValue(value.Priority),
            value.DueAtMillis,
            value.Completed,
            value.CreatedAtMillis,
            value.UpdatedAtMillis,
            RepeatValue(value.ReminderRepeat),
            value.TrashedFromChecklistId,
            value.TrashedFromChecklistName,
            value.TrashedAtMillis,
            value.RemoteVersion);

    private static RemoteAttachment ToRemote(TodoAttachment value, string owner) =>
        new(
            owner,
            value.TodoId,
            value.DeletedAtMillis is null ? value.Id : null,
            value.DeletedAtMillis is null ? value.RemotePath : null,
            value.DeletedAtMillis is null ? value.Sha256 : null,
            value.DeletedAtMillis is null ? value.MimeType : null,
            value.DeletedAtMillis is null ? value.ByteSize : null,
            value.UpdatedAtMillis,
            value.DeletedAtMillis,
            value.RemoteVersion);

    private static bool IsLocallyChanged(SyncState state) =>
        state is SyncState.LocalOnly or SyncState.Dirty;

    private static string PriorityValue(TodoPriority value) => value switch
    {
        TodoPriority.XHigh => "XHIGH",
        TodoPriority.High => "HIGH",
        TodoPriority.Low => "LOW",
        _ => "MEDIUM",
    };

    private static TodoPriority ParsePriority(string value) => value.ToUpperInvariant() switch
    {
        "XHIGH" => TodoPriority.XHigh,
        "HIGH" => TodoPriority.High,
        "LOW" => TodoPriority.Low,
        _ => TodoPriority.Medium,
    };

    private static string RepeatValue(ReminderRepeat value) => value switch
    {
        ReminderRepeat.Daily => "DAILY",
        ReminderRepeat.Weekly => "WEEKLY",
        _ => "NONE",
    };

    private static ReminderRepeat ParseRepeat(string value) => value.ToUpperInvariant() switch
    {
        "DAILY" => ReminderRepeat.Daily,
        "WEEKLY" => ReminderRepeat.Weekly,
        _ => ReminderRepeat.None,
    };

    private static string LanguageValue(LanguageMode value) => value switch
    {
        LanguageMode.English => "en",
        LanguageMode.SimplifiedChinese => "zh-Hans",
        LanguageMode.Arabic => "ar",
        LanguageMode.French => "fr",
        LanguageMode.Russian => "ru",
        LanguageMode.Spanish => "es",
        _ => "system",
    };

    private static LanguageMode ParseLanguage(string value) => value switch
    {
        "en" => LanguageMode.English,
        "zh-Hans" => LanguageMode.SimplifiedChinese,
        "ar" => LanguageMode.Arabic,
        "fr" => LanguageMode.French,
        "ru" => LanguageMode.Russian,
        "es" => LanguageMode.Spanish,
        _ => LanguageMode.System,
    };

    private static string RecordTypeValue(SyncRecordType value) => value switch
    {
        SyncRecordType.Checklist => "checklist",
        SyncRecordType.Todo => "item",
        SyncRecordType.Attachment => "attachment",
        _ => "settings",
    };

    private static SyncRecordType ParseRecordType(string value) =>
        value.ToLowerInvariant() switch
        {
            "checklist" => SyncRecordType.Checklist,
            "todo" or "item" => SyncRecordType.Todo,
            "attachment" => SyncRecordType.Attachment,
            "settings" => SyncRecordType.Settings,
            _ => throw new InvalidOperationException($"Unknown sync record type {value}."),
        };

    private static long? RemoteVersion(string json)
    {
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            return root.TryGetProperty("remote_version", out var value) &&
                   value.ValueKind == JsonValueKind.Number
                ? value.GetInt64()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ImageInfo InspectImage(byte[] bytes)
    {
        if (bytes is not { Length: > 0 and <= 10 * 1024 * 1024 })
        {
            throw new InvalidOperationException("Images must be no larger than 10 MiB.");
        }

        var (contentType, extension) =
            bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff
                ? ("image/jpeg", "jpg")
                : bytes.Length >= 8 &&
                  bytes.AsSpan(0, 8).SequenceEqual(
                      new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    ? ("image/png", "png")
                    : bytes.Length >= 12 &&
                      bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                      bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
                        ? ("image/webp", "webp")
                        : throw new InvalidOperationException(
                            "The selected file must be JPEG, PNG, or WebP.");
        return new ImageInfo(
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            contentType,
            extension);
    }

    private static string SafeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new InvalidOperationException("Cloud attachment identifier is invalid.");
        }

        return value;
    }

    private sealed record ImageInfo(string Sha256, string ContentType, string Extension);

    private sealed record MutationPayload(
        [property: System.Text.Json.Serialization.JsonPropertyName("mutation_uuid")]
        string MutationUuid,
        IReadOnlyList<RemoteChecklist> Checklists,
        IReadOnlyList<RemoteTodo> Items,
        IReadOnlyList<RemoteAttachment> Attachments,
        RemoteSettings? Settings,
        IReadOnlyList<RemoteTombstone> Tombstones,
        [property: System.Text.Json.Serialization.JsonPropertyName("cleaned_image_paths")]
        IReadOnlyList<string> CleanedImagePaths)
    {
        public int RecordCount =>
            Checklists.Count +
            Items.Count +
            Attachments.Count +
            Tombstones.Count +
            (Settings is null ? 0 : 1);

        public bool IsEmpty => RecordCount == 0 && CleanedImagePaths.Count == 0;

        public object RpcBody() => new Dictionary<string, object?>
        {
            ["p_mutation_uuid"] = MutationUuid,
            ["p_client_schema_version"] = SupabaseClient.ExpectedSchema,
            ["p_checklists"] = Checklists,
            ["p_items"] = Items,
            ["p_attachments"] = Attachments,
            ["p_settings"] = Settings,
            ["p_tombstones"] = Tombstones,
            ["p_cleaned_image_paths"] = CleanedImagePaths,
        };
    }
}
