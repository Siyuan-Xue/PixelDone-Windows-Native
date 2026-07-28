using PixelDone.Core;

namespace PixelDone.Core.Tests;

[TestClass]
public sealed class TodoRulesTests
{
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
