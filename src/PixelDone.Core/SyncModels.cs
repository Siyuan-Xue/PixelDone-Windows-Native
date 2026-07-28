namespace PixelDone.Core;

public sealed record SyncMetadata(
    string? OwnerUserId,
    long Cursor,
    string SchemaVersion,
    long? LastSyncAtMillis,
    string? LastError);

public sealed record SettingsSyncRecord(
    PixelDoneSettings Settings,
    long UpdatedAtMillis,
    SyncState SyncState,
    long? RemoteVersion);

public sealed record PendingMutation(
    string Id,
    string PayloadJson,
    long CreatedAtMillis,
    int AttemptCount,
    string? LastError);

public sealed record DueReminder(TodoItem Item, long OccurrenceAtMillis);

public interface ISyncRepository
{
    Task<SyncMetadata> GetSyncMetadataAsync(CancellationToken cancellationToken = default);
    Task<bool> PrepareCloudSessionAsync(
        string ownerUserId,
        long nowMillis,
        CancellationToken cancellationToken = default);
    Task FinishCloudRestoreAsync(CancellationToken cancellationToken = default);
    Task SaveSyncSuccessAsync(
        string ownerUserId,
        long cursor,
        long nowMillis,
        CancellationToken cancellationToken = default);
    Task SaveSyncErrorAsync(string message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoChecklist>> ListDirtyChecklistsAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoItem>> ListDirtyTodosAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoAttachment>> ListDirtyAttachmentsAsync(
        CancellationToken cancellationToken = default);
    Task<SettingsSyncRecord> GetSettingsForSyncAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncTombstone>> ListDirtyTombstonesAsync(
        CancellationToken cancellationToken = default);

    Task ApplyRemoteSettingsAsync(
        LanguageMode language,
        long updatedAtMillis,
        long? remoteVersion,
        long nowMillis,
        CancellationToken cancellationToken = default);
    Task ApplyRemoteTombstoneAsync(
        SyncRecordType recordType,
        string localId,
        CancellationToken cancellationToken = default);
    Task AcknowledgeTombstoneAsync(
        SyncRecordType recordType,
        string localId,
        CancellationToken cancellationToken = default);

    Task RecordConflictAsync(
        SyncConflict conflict,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncConflict>> ListOpenConflictsAsync(
        CancellationToken cancellationToken = default);
    Task ResolveConflictKeepLocalAsync(
        string conflictId,
        long? remoteVersion,
        long nowMillis,
        CancellationToken cancellationToken = default);
    Task MarkConflictResolvedAsync(
        string conflictId,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task SavePendingMutationAsync(
        string mutationId,
        string payloadJson,
        long nowMillis,
        CancellationToken cancellationToken = default);
    Task<PendingMutation?> GetPendingMutationAsync(
        CancellationToken cancellationToken = default);
    Task FailPendingMutationAsync(
        string mutationId,
        string error,
        CancellationToken cancellationToken = default);
    Task ClearPendingMutationAsync(
        string mutationId,
        CancellationToken cancellationToken = default);
}
