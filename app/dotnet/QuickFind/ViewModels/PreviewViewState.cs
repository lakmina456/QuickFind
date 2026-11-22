using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using QuickFind.Services;

namespace QuickFind.ViewModels;

public sealed class PreviewMetaRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class PreviewViewState : INotifyPropertyChanged
{
    string _name = "";
    string _kindLabel = "";
    string _note = "";
    string _text = "";
    ImageSource? _image;
    bool _hasSelection;
    bool _showImage;
    bool _showText;
    bool _showNote;

    public string Name { get => _name; private set => Set(ref _name, value); }
    public string KindLabel { get => _kindLabel; private set => Set(ref _kindLabel, value); }
    public string Note { get => _note; private set => Set(ref _note, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public ImageSource? Image { get => _image; private set => Set(ref _image, value); }
    public bool HasSelection { get => _hasSelection; private set => Set(ref _hasSelection, value); }
    public bool ShowImage { get => _showImage; private set => Set(ref _showImage, value); }
    public bool ShowText { get => _showText; private set => Set(ref _showText, value); }
    public bool ShowNote { get => _showNote; private set => Set(ref _showNote, value); }
    public ObservableCollection<PreviewMetaRow> Rows { get; } = [];

    public void Clear()
    {
        HasSelection = false;
        Name = "";
        KindLabel = "";
        Note = "";
        Text = "";
        Image = null;
        ShowImage = ShowText = ShowNote = false;
        Rows.Clear();
    }

    public void Apply(PreviewInfo info)
    {
        HasSelection = true;
        Name = info.Name;
        KindLabel = info.KindLabel;
        Rows.Clear();
        foreach (var (label, value) in info.Rows)
            Rows.Add(new PreviewMetaRow { Label = label, Value = value });

        ShowImage = info.BodyKind == PreviewBodyKind.Image;
        ShowText = info.BodyKind == PreviewBodyKind.Text;
        ShowNote = info.BodyKind == PreviewBodyKind.Note;
        Image = info.Image;
        Text = info.Text ?? "";
        Note = info.Note ?? "";
    }

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
