using PixelDone.Core;
using PixelDone.Infrastructure;

namespace PixelDone.Core.Tests;

[TestClass]
public sealed class SqliteTodoRepositoryTests
{
    private string? _testRoot;

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (_testRoot is not null && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [TestMethod]
    public async Task CleanDatabase_SeedsSystemLists_AndPersistsSettings()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        var checklists = await repository.ListChecklistsAsync();
        CollectionAssert.AreEqual(
            new[]
            {
                PixelDoneChecklists.MainId,
                PixelDoneChecklists.TrashId,
                PixelDoneChecklists.SettingsId,
            },
            checklists.Select(item => item.Id).ToArray());

        var settings = new PixelDoneSettings(
            Theme: ThemeMode.Dark,
            Language: LanguageMode.Arabic,
            HideCompleted: true,
            DockActions: [DockAction.Sort, DockAction.ExportMarkdown],
            DockPlusPlacement: DockPlusPlacement.RightEdge);
        await repository.SaveSettingsAsync(settings);

        var restored = await repository.GetSettingsAsync();
        Assert.AreEqual(ThemeMode.Dark, restored.Theme);
        Assert.AreEqual(LanguageMode.Arabic, restored.Language);
        Assert.IsTrue(restored.HideCompleted);
        CollectionAssert.AreEqual(
            new[] { DockAction.Sort, DockAction.ExportMarkdown },
            restored.EffectiveDockActions.ToArray());
        Assert.AreEqual(DockPlusPlacement.RightEdge, restored.DockPlusPlacement);
    }

    [TestMethod]
    public async Task Todo_CanMoveToTrash_Restore_AndDeleteForever()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var work = new TodoChecklist("work", "WORK", 1, now, now);
        await repository.UpsertChecklistAsync(work);
        var todo = TodoRules.Create(
            "todo-1",
            "ship native app",
            TodoPriority.XHigh,
            now + 60_000,
            now,
            checklistId: work.Id);
        Assert.IsNotNull(todo);
        await repository.UpsertAsync(todo);

        await repository.DeleteAsync(todo.Id, now + 1);
        Assert.IsEmpty(await repository.ListAsync(work.Id));
        var trash = await repository.ListAsync(PixelDoneChecklists.TrashId);
        Assert.HasCount(1, trash);
        Assert.AreEqual("WORK", trash[0].TrashedFromChecklistName);

        await repository.RestoreAsync(todo.Id, now + 2);
        Assert.HasCount(1, await repository.ListAsync(work.Id));

        await repository.DeleteAsync(todo.Id, now + 3);
        await repository.DeletePermanentlyAsync(todo.Id, now + 4);
        Assert.IsEmpty(await repository.ListAsync(PixelDoneChecklists.TrashId));
    }

    [TestMethod]
    public async Task DeletingChecklist_MovesTodosToTrash_AndKeepsOneNormalList()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var work = new TodoChecklist("work", "WORK", 1, now, now);
        await repository.UpsertChecklistAsync(work);
        var todo = TodoRules.Create(
            "todo-1",
            "move with list",
            TodoPriority.Medium,
            0,
            now,
            checklistId: work.Id);
        Assert.IsNotNull(todo);
        await repository.UpsertAsync(todo);

        await repository.DeleteChecklistAsync(work.Id, now + 1);

        Assert.IsFalse((await repository.ListChecklistsAsync()).Any(item => item.Id == work.Id));
        var trash = await repository.ListAsync(PixelDoneChecklists.TrashId);
        Assert.HasCount(1, trash);
        Assert.AreEqual(work.Id, trash[0].TrashedFromChecklistId);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => repository.DeleteChecklistAsync(PixelDoneChecklists.MainId, now + 2));
    }

    [TestMethod]
    public async Task FirstCloudSession_DiscardsUnsyncedWorkspace_AndStartsAtCursorZero()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await repository.UpsertChecklistAsync(
            new TodoChecklist("local", "LOCAL", 1, now, now));
        var changed = await repository.PrepareCloudSessionAsync("owner-1", now + 1);

        Assert.IsTrue(changed);
        Assert.IsFalse(
            (await repository.ListChecklistsAsync()).Any(value => value.Id == "local"));
        var metadata = await repository.GetSyncMetadataAsync();
        Assert.AreEqual("owner-1", metadata.OwnerUserId);
        Assert.AreEqual(0, metadata.Cursor);
        Assert.AreEqual("3.2", metadata.SchemaVersion);
    }

    [TestMethod]
    public async Task Attachment_AndReminder_AreStoredWithNativeBaseline()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var due = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1_000;
        var todo = TodoRules.Create(
            "todo-reminder",
            "native reminder",
            TodoPriority.XHigh,
            due,
            due - 10_000,
            ReminderRepeat.Daily);
        Assert.IsNotNull(todo);
        await repository.UpsertAsync(todo);
        await repository.UpsertAttachmentAsync(
            new TodoAttachment(
                "attachment-1",
                todo.Id,
                @"C:\images\task.png",
                null,
                new string('a', 64),
                "image/png",
                100,
                due));

        Assert.IsNotNull(await repository.GetAttachmentAsync(todo.Id));
        var reminders = await repository.ListDueRemindersAsync(due + 1_000);
        Assert.HasCount(1, reminders);
        await repository.MarkReminderDeliveredAsync(
            todo.Id,
            reminders[0].OccurrenceAtMillis,
            due + 1_000);
        Assert.IsEmpty(await repository.ListDueRemindersAsync(due + 1_000));
        Assert.AreEqual(
            reminders[0].OccurrenceAtMillis + TodoRules.DailyIntervalMillis,
            (await repository.GetAsync(todo.Id))?.DueAtMillis);
    }

    [TestMethod]
    public async Task SyncRepository_TracksDirtyRecords_AndIdempotentMutation()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var todo = TodoRules.Create(
            "todo-sync",
            "sync me",
            TodoPriority.High,
            0,
            now);
        Assert.IsNotNull(todo);
        await repository.UpsertAsync(todo);

        Assert.IsNotEmpty(await repository.ListDirtyChecklistsAsync());
        Assert.HasCount(1, await repository.ListDirtyTodosAsync());
        await repository.SavePendingMutationAsync("mutation-1", """{"value":1}""", now);
        await repository.SavePendingMutationAsync("mutation-1", """{"value":2}""", now + 1);
        var pending = await repository.GetPendingMutationAsync();
        Assert.IsNotNull(pending);
        Assert.AreEqual("""{"value":1}""", pending.PayloadJson);
        await repository.ClearPendingMutationAsync("mutation-1");
        Assert.IsNull(await repository.GetPendingMutationAsync());
    }

    [TestMethod]
    public async Task BatchDelete_IsAtomicAndScopedToSelectedChecklist()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await repository.UpsertChecklistAsync(
            new TodoChecklist("work", "WORK", 1, now, now));
        foreach (var item in new[]
                 {
                     TodoRules.Create("main-a", "A", TodoPriority.Medium, 0, now),
                     TodoRules.Create("main-b", "B", TodoPriority.Medium, 0, now),
                     TodoRules.Create(
                         "work-a",
                         "C",
                         TodoPriority.Medium,
                         0,
                         now,
                         checklistId: "work"),
                 })
        {
            Assert.IsNotNull(item);
            await repository.UpsertAsync(item);
        }

        Assert.AreEqual(
            1,
            await repository.MoveManyToTrashAsync(
                PixelDoneChecklists.MainId,
                ["main-a", "work-a"],
                now + 1));
        Assert.HasCount(1, await repository.ListAsync(PixelDoneChecklists.MainId));
        Assert.HasCount(1, await repository.ListAsync("work"));
        Assert.HasCount(1, await repository.ListAsync(PixelDoneChecklists.TrashId));
    }

    private SqliteTodoRepository CreateRepository()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "PixelDone.Tests",
            Guid.NewGuid().ToString("N"));
        return new SqliteTodoRepository(Path.Combine(_testRoot, "pixeldone.sqlite3"));
    }
}
