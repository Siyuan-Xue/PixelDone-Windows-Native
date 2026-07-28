using System.Text.Json.Serialization;
using System.Reflection;

namespace PixelDone.Infrastructure;

public sealed record AuthSession(
    string UserId,
    string? Email,
    string AccessToken,
    string RefreshToken,
    long ExpiresAtMillis);

public sealed record SupabaseConfig(
    string BaseUrl,
    string PublishableKey,
    bool AllowInsecureHttp = false)
{
    public static SupabaseConfig FromEnvironment()
    {
        var baseUrl =
            Environment.GetEnvironmentVariable("PIXELDONE_SUPABASE_URL") ??
            BuildMetadata.Value("PixelDoneSupabaseUrl");
        var publishableKey =
            Environment.GetEnvironmentVariable("PIXELDONE_SUPABASE_PUBLISHABLE_KEY") ??
            BuildMetadata.Value("PixelDoneSupabasePublishableKey");
        var allowInsecure =
            Environment.GetEnvironmentVariable("PIXELDONE_ALLOW_INSECURE_HTTP") is
                "1" or "true" or "TRUE" or "yes" or "YES";
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(publishableKey))
        {
            throw new InvalidOperationException("PixelDone cloud is not configured.");
        }

        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException(
                "PixelDone cloud requires HTTPS. HTTP needs explicit development opt-in.");
        }

        return new SupabaseConfig(baseUrl, publishableKey.Trim(), allowInsecure);
    }
}

internal static class BuildMetadata
{
    public static string? Value(string key) =>
        typeof(BuildMetadata).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value;
}

public sealed record RemoteSnapshot(
    IReadOnlyList<RemoteChecklist>? Checklists = null,
    IReadOnlyList<RemoteTodo>? Items = null,
    IReadOnlyList<RemoteAttachment>? Attachments = null)
{
    public IReadOnlyList<RemoteChecklist> EffectiveChecklists => Checklists ?? [];
    public IReadOnlyList<RemoteTodo> EffectiveItems => Items ?? [];
    public IReadOnlyList<RemoteAttachment> EffectiveAttachments => Attachments ?? [];
}

public sealed record RemoteChangeBatch(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("server_version")] long ServerVersion,
    IReadOnlyList<RemoteChecklist>? Checklists = null,
    IReadOnlyList<RemoteTodo>? Items = null,
    IReadOnlyList<RemoteAttachment>? Attachments = null,
    RemoteSettings? Settings = null,
    IReadOnlyList<RemoteTombstone>? Tombstones = null,
    [property: JsonPropertyName("image_cleanup_paths")]
    IReadOnlyList<string>? ImageCleanupPaths = null)
{
    public IReadOnlyList<RemoteChecklist> EffectiveChecklists => Checklists ?? [];
    public IReadOnlyList<RemoteTodo> EffectiveItems => Items ?? [];
    public IReadOnlyList<RemoteAttachment> EffectiveAttachments => Attachments ?? [];
    public IReadOnlyList<RemoteTombstone> EffectiveTombstones => Tombstones ?? [];
    public IReadOnlyList<string> EffectiveImageCleanupPaths => ImageCleanupPaths ?? [];
}

public sealed record RemotePushResult(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("server_version")] long ServerVersion,
    RemoteSnapshot? Accepted = null,
    RemoteSettings? Settings = null,
    IReadOnlyList<RemoteTombstone>? Tombstones = null,
    IReadOnlyList<RemoteConflict>? Conflicts = null,
    [property: JsonPropertyName("image_cleanup_paths")]
    IReadOnlyList<string>? ImageCleanupPaths = null)
{
    public RemoteSnapshot EffectiveAccepted => Accepted ?? new RemoteSnapshot();
    public IReadOnlyList<RemoteTombstone> EffectiveTombstones => Tombstones ?? [];
    public IReadOnlyList<RemoteConflict> EffectiveConflicts => Conflicts ?? [];
    public IReadOnlyList<string> EffectiveImageCleanupPaths => ImageCleanupPaths ?? [];
}

public sealed record RemoteChecklist(
    [property: JsonPropertyName("local_id")] string LocalId,
    string? Id,
    [property: JsonPropertyName("owner_user_id")] string OwnerUserId,
    [property: JsonPropertyName("sort_index")] int SortIndex,
    string Name,
    [property: JsonPropertyName("created_at_millis")] long CreatedAtMillis,
    [property: JsonPropertyName("updated_at_millis")] long UpdatedAtMillis,
    [property: JsonPropertyName("remote_version")] long? RemoteVersion);

public sealed record RemoteTodo(
    [property: JsonPropertyName("local_id")] string LocalId,
    string? Id,
    [property: JsonPropertyName("owner_user_id")] string OwnerUserId,
    [property: JsonPropertyName("checklist_local_id")] string ChecklistLocalId,
    [property: JsonPropertyName("sort_index")] int SortIndex,
    string Title,
    string Priority,
    [property: JsonPropertyName("due_at_millis")] long DueAtMillis,
    bool Completed,
    [property: JsonPropertyName("created_at_millis")] long CreatedAtMillis,
    [property: JsonPropertyName("updated_at_millis")] long UpdatedAtMillis,
    [property: JsonPropertyName("reminder_repeat")] string ReminderRepeat,
    [property: JsonPropertyName("trashed_from_checklist_id")]
    string? TrashedFromChecklistId,
    [property: JsonPropertyName("trashed_from_checklist_name")]
    string? TrashedFromChecklistName,
    [property: JsonPropertyName("trashed_at_millis")] long? TrashedAtMillis,
    [property: JsonPropertyName("remote_version")] long? RemoteVersion);

public sealed record RemoteAttachment(
    [property: JsonPropertyName("owner_user_id")] string? OwnerUserId,
    [property: JsonPropertyName("todo_local_id")] string TodoLocalId,
    [property: JsonPropertyName("attachment_id")] string? AttachmentId,
    [property: JsonPropertyName("object_path")] string? ObjectPath,
    [property: JsonPropertyName("content_sha256")] string? ContentSha256,
    [property: JsonPropertyName("content_type")] string? ContentType,
    [property: JsonPropertyName("byte_size")] long? ByteSize,
    [property: JsonPropertyName("updated_at_millis")] long UpdatedAtMillis,
    [property: JsonPropertyName("deleted_at_millis")] long? DeletedAtMillis,
    [property: JsonPropertyName("remote_version")] long? RemoteVersion);

public sealed record RemoteSettings(
    [property: JsonPropertyName("owner_user_id")] string? OwnerUserId,
    [property: JsonPropertyName("language_mode")] string LanguageMode,
    [property: JsonPropertyName("updated_at_millis")] long UpdatedAtMillis,
    [property: JsonPropertyName("remote_version")] long? RemoteVersion);

public sealed record RemoteTombstone(
    [property: JsonPropertyName("owner_user_id")] string? OwnerUserId,
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("local_id")] string LocalId,
    [property: JsonPropertyName("deleted_at_millis")] long DeletedAtMillis,
    [property: JsonPropertyName("remote_version")] long? RemoteVersion);

public sealed record RemoteConflict(
    [property: JsonPropertyName("record_type")] string RecordType,
    [property: JsonPropertyName("local_id")] string LocalId,
    string Message);

public sealed record SyncSummary(
    bool FirstCloudRestore,
    int PulledRecords,
    int PushedRecords,
    int Conflicts,
    long ServerVersion);
