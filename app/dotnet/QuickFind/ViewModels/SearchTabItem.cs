using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuickFind.Core;

namespace QuickFind.ViewModels;

public sealed class SearchTabItem : INotifyPropertyChanged
{
    string _title = "Search";
    public string Title
    {
        get => _title;
        private set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    public string Query { get; set; } = "";
    public int FilterIndex { get; set; }
    public string SizeChip { get; set; } = "";
    public string DateChip { get; set; } = "";
    public string? LocationRoot { get; set; }
    public SortKey Sort { get; set; } = SortKey.Name;
    public bool Ascending { get; set; } = true;

    bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnPropertyChanged(); } }
    }

    public void RefreshTitle()
    {
        var q = Query?.Trim() ?? "";
        Title = string.IsNullOrEmpty(q) ? "Search" : (q.Length > 24 ? q[..24] + "…" : q);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
