using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickFind.ViewModels;

public sealed class LocationNavItem : INotifyPropertyChanged
{
    bool _selected;
    string _countText = "";

    public LocationNavItem(string id, string label, string? root, string glyph)
    {
        Id = id;
        Label = label;
        Root = root;
        Glyph = glyph;
    }

    public string Id { get; }
    public string Label { get; }
    public string? Root { get; }
    public string Glyph { get; }

    public string CountText
    {
        get => _countText;
        set
        {
            if (_countText == value) return;
            _countText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
        }
    }

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
