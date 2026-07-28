using PixelDone.Core;

namespace PixelDone.Core.Tests;

[TestClass]
public sealed class TodoRulesTests
{
    [TestMethod]
    public void DockSelectionIsUniqueBoundedAndDropsOldestOnFifth()
    {
        var selected = new[]
        {
            DockAction.Sort,
            DockAction.Ddl,
            DockAction.HideDone,
            DockAction.CleanDone,
        };

        CollectionAssert.AreEqual(
            new[]
            {
                DockAction.Ddl,
                DockAction.HideDone,
                DockAction.CleanDone,
                DockAction.ExportMarkdown,
            },
            DockRules.Toggle(selected, DockAction.ExportMarkdown).ToArray());
        CollectionAssert.AreEqual(
            new[] { DockAction.Sort, DockAction.Ddl },
            DockRules.Normalize(
                [DockAction.Sort, DockAction.Sort, DockAction.Ddl]).ToArray());
    }

    [TestMethod]
    public void ProductVersionOrdersBetaBeforeStable()
    {
        Assert.IsTrue(ProductVersion.TryParse("v4.0.0-beta.2", out var beta));
        Assert.IsTrue(ProductVersion.TryParse("4.0.0", out var stable));
        Assert.IsTrue(ProductVersion.TryParse("4.1.0-beta.1", out var next));
        Assert.AreEqual(-1, Math.Sign(beta.CompareTo(stable)));
        Assert.AreEqual(-1, Math.Sign(stable.CompareTo(next)));
    }

    [TestMethod]
    public void Create_TrimsTitle_AndUsesMainChecklist()
    {
        var item = TodoRules.Create("todo-1", "  ship beta  ", TodoPriority.High, 42, 10);

        Assert.IsNotNull(item);
        Assert.AreEqual("ship beta", item.Title);
        Assert.AreEqual(PixelDoneChecklists.MainId, item.ChecklistId);
    }

    [TestMethod]
    public void Create_RejectsBlankTitle()
    {
        Assert.IsNull(TodoRules.Create("todo-1", "  ", TodoPriority.Low, 0, 10));
    }

    [TestMethod]
    public void Visible_SortsPriorityBeforeDeadline()
    {
        var low = Item("low", TodoPriority.Low, 100);
        var urgent = Item("urgent", TodoPriority.XHigh, 200);

        var visible = TodoRules.Visible([low, urgent], TodoSortMode.Priority, false);

        CollectionAssert.AreEqual(new[] { "urgent", "low" }, visible.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void NextReminderAt_AdvancesDailyOccurrence()
    {
        const long day = 24L * 60L * 60L * 1000L;
        var item = Item("daily", TodoPriority.Medium, 100) with
        {
            ReminderRepeat = ReminderRepeat.Daily,
        };

        Assert.AreEqual(100 + (3 * day), TodoRules.NextReminderAt(item, 100 + (2 * day) + 1));
    }

    [TestMethod]
    public void TrashRules_PreserveOrigin_AndRestoreIdentity()
    {
        var item = Item("task", TodoPriority.High, 100) with
        {
            ChecklistId = "work",
        };

        var trashed = TodoRules.MoveToTrash(item, "WORK", 200);
        var restored = TodoRules.RestoreFromTrash(trashed, "work", 300);

        Assert.AreEqual(PixelDoneChecklists.TrashId, trashed.ChecklistId);
        Assert.AreEqual("work", trashed.TrashedFromChecklistId);
        Assert.AreEqual("WORK", trashed.TrashedFromChecklistName);
        Assert.AreEqual("work", restored.ChecklistId);
        Assert.IsNull(restored.TrashedAtMillis);
        Assert.IsNull(restored.TrashedFromChecklistId);
    }

    [TestMethod]
    public void TrashRules_ExpireAtExactlyThirtyDays()
    {
        const long day = 24L * 60L * 60L * 1000L;
        var item = Item("task", TodoPriority.Low, 0) with
        {
            ChecklistId = PixelDoneChecklists.TrashId,
            TrashedAtMillis = 10,
        };

        Assert.IsFalse(TodoRules.IsTrashExpired(item, 10 + (30 * day) - 1));
        Assert.IsTrue(TodoRules.IsTrashExpired(item, 10 + (30 * day)));
    }

    private static TodoItem Item(string id, TodoPriority priority, long dueAt) =>
        new(
            id,
            PixelDoneChecklists.MainId,
            id,
            priority,
            dueAt,
            false,
            1,
            1);
}
