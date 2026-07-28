using CommunityToolkit.Mvvm.ComponentModel;
using PixelDone.Core;

namespace PixelDone.Windows.ViewModels;

public sealed class ChecklistViewModel(TodoChecklist model) : ObservableObject
{
    public TodoChecklist Model { get; private set; } = model;

    public string Id => Model.Id;
    public string Name => Model.Name;
    public bool IsTrash => Id == PixelDoneChecklists.TrashId;
    public bool IsSettings => Id == PixelDoneChecklists.SettingsId;
    public bool CanEdit => !Model.IsSystem;
    public string Glyph => IsTrash ? "×" : IsSettings ? "⚙" : "■";
    public string DisplayName => $"{Glyph}  {Name}";
}
