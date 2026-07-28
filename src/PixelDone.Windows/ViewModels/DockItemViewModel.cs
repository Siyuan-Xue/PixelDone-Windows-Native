using PixelDone.Core;

namespace PixelDone.Windows.ViewModels;

public sealed record DockItemViewModel(
    bool IsAdd,
    DockAction? Action,
    string Label)
{
    public static DockItemViewModel Add { get; } = new(true, null, "+ ADD");

    public static DockItemViewModel ForAction(
        DockAction action,
        TodoSortMode sortMode,
        bool showDdl,
        bool hideCompleted,
        bool quickDelete,
        bool batchMode) =>
        new(
            false,
            action,
            action switch
            {
                DockAction.Sort => sortMode == TodoSortMode.Priority
                    ? "SORT · PRIORITY"
                    : "SORT · TIME",
                DockAction.Ddl => showDdl ? "DDL · ON" : "DDL · OFF",
                DockAction.HideDone => hideCompleted ? "DONE · HIDDEN" : "DONE · SHOWN",
                DockAction.CleanDone => "CLEAN DONE",
                DockAction.QuickDelete => quickDelete
                    ? "QUICK DELETE · ON"
                    : "QUICK DELETE · OFF",
                DockAction.BatchDelete => batchMode ? "BATCH · CANCEL" : "BATCH DELETE",
                DockAction.ExportMarkdown => "COPY MD",
                _ => action.ToString().ToUpperInvariant(),
            });
}

public sealed record DockActionChoiceViewModel(
    DockAction Action,
    string Label,
    bool IsSelected,
    int Order)
{
    public string OrderLabel => IsSelected ? (Order + 1).ToString() : "—";
    public bool CanMoveUp => IsSelected && Order > 0;
    public bool CanMoveDown => IsSelected && Order < DockRules.MaxActions - 1;
}
