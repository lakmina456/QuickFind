using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickFind.ViewModels;

public sealed class FilterPillItem : INotifyPropertyChanged
{
    bool _selected;

    public FilterPillItem(string label, int index)
    {
        Label = label;
        Index = index;
    }

    public string Label { get; }
    public int Index { get; }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
