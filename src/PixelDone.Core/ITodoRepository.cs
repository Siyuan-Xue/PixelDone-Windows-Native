namespace PixelDone.Core;

public interface ITodoRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoItem>> ListAsync(
        string checklistId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(TodoItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
