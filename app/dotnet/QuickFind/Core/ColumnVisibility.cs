using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickFind.Core;

public sealed class ColumnVisibility : INotifyPropertyChanged
{
    bool _path = true, _size = true, _extension, _modified = true, _created, _attributes;

    public bool Path { get => _path; set => Set(ref _path, value); }
    public bool Size { get => _size; set => Set(ref _size, value); }
    public bool Extension { get => _extension; set => Set(ref _extension, value); }
    public bool Modified { get => _modified; set => Set(ref _modified, value); }
    public bool Created { get => _created; set => Set(ref _created, value); }
    public bool Attributes { get => _attributes; set => Set(ref _attributes, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    bool Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
