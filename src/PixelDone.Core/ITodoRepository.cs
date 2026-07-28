namespace PixelDone.Core;

public interface ITodoRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoChecklist>> ListChecklistsAsync(
        CancellationToken cancellationToken = default);

    Task UpsertChecklistAsync(
        TodoChecklist checklist,
        CancellationToken cancellationToken = default);

    Task DeleteChecklistAsync(
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoItem>> ListAsync(
        string checklistId,
        CancellationToken cancellationToken = default);

    Task<TodoItem?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(TodoItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task MoveToChecklistAsync(
        string id,
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task DeletePermanentlyAsync(
        string id,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<int> MoveCompletedToTrashAsync(
        string checklistId,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<int> MoveManyToTrashAsync(
        string checklistId,
        IReadOnlyCollection<string> ids,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<int> PurgeTrashAsync(
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<PixelDoneSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(
        PixelDoneSettings settings,
        CancellationToken cancellationToken = default);

    Task<TodoAttachment?> GetAttachmentAsync(
        string todoId,
        CancellationToken cancellationToken = default);

    Task UpsertAttachmentAsync(
        TodoAttachment attachment,
        CancellationToken cancellationToken = default);

    Task RemoveAttachmentAsync(
        string todoId,
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DueReminder>> ListDueRemindersAsync(
        long nowMillis,
        CancellationToken cancellationToken = default);

    Task MarkReminderDeliveredAsync(
        string todoId,
        long occurrenceAtMillis,
        long deliveredAtMillis,
        CancellationToken cancellationToken = default);
}
