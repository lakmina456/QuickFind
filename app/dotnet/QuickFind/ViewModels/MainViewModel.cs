using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QuickFind.Core;
using QuickFind.Indexing;
using QuickFind.Services;
using DriveInfo = System.IO.DriveInfo;

namespace QuickFind.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    /// <summary>Max rows kept for the DataGrid window. MatchTotal still reports the full hit count.</summary>
    const int UiRowCap = 2_500;

    readonly FileIndexStore _store = new();
    readonly ProgressState _progress = new();
    readonly HybridIndexSource _source;
    readonly SearchService _search;
    readonly IndexOptions _indexOpts;
    readonly DispatcherTimer _debounce;
    readonly DispatcherTimer _idleResearch;
    readonly DispatcherTimer _persistUi;
    readonly CancellationTokenSource _cts = new();
    readonly Action _onIndexChanged;
    readonly Action _onProgressChanged;

    string _lastIndexModeLabel = "\0";
    string _lastIndexPathLabel = "\0";
    string _lastIndexFoundLabel = "\0";
    string _lastMatchCountText = "\0";
    string _lastMatchCountLabel = "\0";
    bool _lastIsIndexing;
    bool _lastHasSelection;
    double _lastIndexProgressFraction = double.NaN;

    ulong _serial;
    ulong _shownSerial;
    ulong _idsEpoch;
    ulong _lastSeenGeneration = ulong.MaxValue;
    ulong _lastSeenEpoch;
    ulong _locationCountGeneration = ulong.MaxValue;
    DateTime _lastSent = DateTime.MinValue;
    string _lastSearchText = "\0";
    SearchOptions _lastOpts = new();
    SortKey _lastSort = SortKey.Name;
    bool _lastAsc = true;
    string? _statusMessage;
    DateTime _statusMessageAt;
    bool _searching;
    bool _countingLocations;
    long _indexedCount;
    int _matchTotal;
    List<uint> _lastIds = [];
    string? _locationRoot;

    public MainViewModel()
    {
        Results = new VirtualResultList(_store);
        _search = new SearchService(_store);
        _search.ReplyReady += OnReply;

        Bookmarks = BookmarkStore.Load();
        History = SearchHistoryStore.Load();
        FilterLabels = Filters.FilterList.Select(f => f.Label).ToList();
        FilterPills =
        [
            new("All", 0),
            new("Folders", 5),
            new("Documents", 3),
            new("Pictures", 6),
            new("Audio", 1),
            new("Video", 7),
            new("Archives", 2),
            new("Apps", 4)
        ];
        SyncFilterPills();
        RebuildLocations();

        var ui = UiSettingsStore.Load();
        _dark = ui.Dark;
        _compactRows = ui.CompactRows;
        _showPreview = ui.Preview;
        _statusBar = ui.StatusBar;
        _hotkeyEnabled = ui.HotkeyEnabled;
        _closeToTray = ui.CloseToTray;
        _indexOpts = UiSettingsStore.ToIndexOptions(ui);
        _indexOpts.StartWithWindows = ui.StartWithWindows;
        if (ui.StartWithWindows != StartupManager.IsEnabled())
            StartupManager.SetEnabled(ui.StartWithWindows);
        UiSettingsStore.ApplyTo(Columns, ui);
        ThemeService.Apply(_dark, _indexOpts.Accent);
        _source = new HybridIndexSource(_indexOpts);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); PumpSearch(force: true); };

        // Coalesce index events into one re-search — no polling loop.
        _idleResearch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _idleResearch.Tick += (_, _) =>
        {
            _idleResearch.Stop();
            if (HasActiveSearch()) PumpSearch(force: true);
            else RefreshIndexedCount();
        };

        _persistUi = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _persistUi.Tick += (_, _) => { _persistUi.Stop(); PersistUiNow(); };

        _onIndexChanged = () => Application.Current?.Dispatcher.BeginInvoke(OnIndexChanged, DispatcherPriority.Background);
        _onProgressChanged = () => Application.Current?.Dispatcher.BeginInvoke(OnProgressChanged, DispatcherPriority.Background);
        _store.Changed += _onIndexChanged;
        _progress.Changed += _onProgressChanged;

        ExitCommand = new RelayCommand(_ =>
        {
            if (ExitAppRequested is not null) ExitAppRequested.Invoke();
            else Application.Current.Shutdown();
        });
        ExportCommand = new RelayCommand(_ => Export());
        CopyFullNameCommand = new RelayCommand(_ => CopySelectedFull());
        SelectAllCommand = new RelayCommand(_ => SelectAll());
        RebuildCommand = new RelayCommand(_ => _source.Rebuild(_store, _progress, _cts.Token));
        AddBookmarkCommand = new RelayCommand(_ => AddBookmark(), _ => CanAddBookmark);
        RemoveBookmarkCommand = new RelayCommand(_ => RemoveBookmark());
        OpenHelpCommand = new RelayCommand(_ => ShowHelp = true);
        OpenAboutCommand = new RelayCommand(_ => ShowAbout = true);
        OpenSelectedCommand = new RelayCommand(_ => OpenSelected(), _ => HasSelection);
        OpenPathCommand = new RelayCommand(_ => OpenSelectedPath(), _ => HasSelection);
        CopyPathCommand = new RelayCommand(_ => CopySelectedPath(), _ => HasSelection);
        CopyToCommand = new RelayCommand(_ => CopyTo(), _ => HasSelection);
        MoveToCommand = new RelayCommand(_ => MoveTo(), _ => HasSelection);
        ZipSelectedCommand = new RelayCommand(_ => ZipSelected(), _ => HasSelection);
        BatchRenameCommand = new RelayCommand(_ => BatchRename(), _ => HasSelection);
        ApplyBookmarkCommand = new RelayCommand<Bookmark>(b =>
        {
            if (b is null) return;
            Query = b.Query;
            FilterIndex = b.Filter;
            FocusSearchRequested?.Invoke();
        });
        ApplyHistoryCommand = new RelayCommand<HistoryEntry>(ApplyHistory);
        PinHistoryCommand = new RelayCommand<HistoryEntry>(PinHistory);
        ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
        QuickViewCommand = new RelayCommand(p => ApplyQuickView(p as string));
        NewTabCommand = new RelayCommand(_ => NewTab());
        CloseTabCommand = new RelayCommand(p => CloseTab(p as SearchTabItem));
        SelectTabCommand = new RelayCommand(p => SelectTab(p as SearchTabItem));
        SelectLocationCommand = new RelayCommand(p =>
        {
            if (p is LocationNavItem loc) SelectLocation(loc.Root);
            else if (p is string s) SelectLocation(string.IsNullOrEmpty(s) ? null : s);
            else SelectLocation(null);
        });
        SetFilterCommand = new RelayCommand(p =>
        {
            if (p is int i) FilterIndex = i;
            else if (p is not null && int.TryParse(p.ToString(), out var j)) FilterIndex = j;
        });
        ClearQueryCommand = new RelayCommand(_ => Query = "");
        ClearRefineCommand = new RelayCommand(_ => { SizeChip = ""; DateChip = ""; });
        ClearSearchAndFiltersCommand = new RelayCommand(_ =>
        {
            Query = "";
            FilterIndex = 0;
            SizeChip = "";
            DateChip = "";
            SelectLocation(null);
        });
        ToggleThemeCommand = new RelayCommand(_ => IsDark = !IsDark);
        SetDensityCommand = new RelayCommand(p => CompactRows = string.Equals(p as string, "compact", StringComparison.OrdinalIgnoreCase));
        RunAsAdminCommand = new RelayCommand(_ => Elevation.RelaunchAsAdmin());
        TogglePaletteCommand = new RelayCommand(_ => ShowPalette = !ShowPalette);

        Columns.PropertyChanged += (_, _) => PersistUi();
        BuildPaletteCommands();
        LoadTabs(ui);

        _source.Start(_store, _progress, _cts.Token);
        ScheduleSearch();
        RefreshIndexedCount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? FocusSearchRequested;
    public event Action? ShowWindowRequested;
    public event Action? ExitAppRequested;

    /// <summary>Virtualized result window — builds rows on demand (avoids materializing millions).</summary>
    public VirtualResultList Results { get; }
    public ObservableCollection<LocationNavItem> Locations { get; } = [];
    public ObservableCollection<SearchTabItem> Tabs { get; } = [];
    public List<Bookmark> Bookmarks { get; private set; }
    public List<HistoryEntry> History { get; private set; }
    public List<string> FilterLabels { get; }
    public List<FilterPillItem> FilterPills { get; }
    public SearchOptions Options { get; } = new();
    public ColumnVisibility Columns { get; } = new();
    public PreviewViewState Preview { get; } = new();
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool FolderSizesReady { get; private set; } = true;

    string _query = "";
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value)) return;
            ClearSelection();
            ScheduleSearch();
            UpdateTitle();
            OnPropertyChanged(nameof(HasQuery));
            UpdateOverlay();
            SyncActiveTab();
        }
    }

    public bool HasQuery => !string.IsNullOrEmpty(_query);

    string _sizeChip = "";
    public string SizeChip
    {
        get => _sizeChip;
        set
        {
            var v = value ?? "";
            if (!Set(ref _sizeChip, v)) return;
            OnPropertyChanged(nameof(SizeChip10));
            OnPropertyChanged(nameof(SizeChip100));
            OnPropertyChanged(nameof(SizeChip1g));
            OnPropertyChanged(nameof(HasRefineFilters));
            ScheduleSearch();
            SyncActiveTab();
        }
    }

    public bool HasRefineFilters => !string.IsNullOrEmpty(_sizeChip) || !string.IsNullOrEmpty(_dateChip);

    public bool SizeChip10
    {
        get => SizeChip == "size:>10mb";
        set => SizeChip = value ? "size:>10mb" : (SizeChip == "size:>10mb" ? "" : SizeChip);
    }
    public bool SizeChip100
    {
        get => SizeChip == "size:>100mb";
        set => SizeChip = value ? "size:>100mb" : (SizeChip == "size:>100mb" ? "" : SizeChip);
    }
    public bool SizeChip1g
    {
        get => SizeChip == "size:>1gb";
        set => SizeChip = value ? "size:>1gb" : (SizeChip == "size:>1gb" ? "" : SizeChip);
    }

    string _dateChip = "";
    public string DateChip
    {
        get => _dateChip;
        set
        {
            var v = value ?? "";
            if (!Set(ref _dateChip, v)) return;
            OnPropertyChanged(nameof(DateChipToday));
            OnPropertyChanged(nameof(DateChipWeek));
            OnPropertyChanged(nameof(DateChipMonth));
            OnPropertyChanged(nameof(HasRefineFilters));
            ScheduleSearch();
            SyncActiveTab();
        }
    }

    public bool DateChipToday
    {
        get => DateChip == "dm:today";
        set => DateChip = value ? "dm:today" : (DateChip == "dm:today" ? "" : DateChip);
    }
    public bool DateChipWeek
    {
        get => DateChip == "dm:thisweek";
        set => DateChip = value ? "dm:thisweek" : (DateChip == "dm:thisweek" ? "" : DateChip);
    }
    public bool DateChipMonth
    {
        get => DateChip == "dm:thismonth";
        set => DateChip = value ? "dm:thismonth" : (DateChip == "dm:thismonth" ? "" : DateChip);
    }

    bool _contentSearch;
    public bool ContentSearchEnabled
    {
        get => _contentSearch;
        set { if (Set(ref _contentSearch, value)) ScheduleSearch(); }
    }

    bool _hotkeyEnabled = true;
    public bool HotkeyEnabled
    {
        get => _hotkeyEnabled;
        set { if (Set(ref _hotkeyEnabled, value)) { PersistUi(); HotkeyChanged?.Invoke(); } }
    }

    bool _closeToTray = true;
    public bool CloseToTray
    {
        get => _closeToTray;
        set { if (Set(ref _closeToTray, value)) PersistUi(); }
    }

    public event Action? HotkeyChanged;

    bool _dropHighlight;
    public bool DropHighlight
    {
        get => _dropHighlight;
        set => Set(ref _dropHighlight, value);
    }

    SearchTabItem? _activeTab;
    public SearchTabItem? ActiveTab
    {
        get => _activeTab;
        private set => Set(ref _activeTab, value);
    }

    int _filterIndex;
    public int FilterIndex
    {
        get => _filterIndex;
        set
        {
            if (!Set(ref _filterIndex, value)) return;
            ClearSelection();
            SyncFilterPills();
            ScheduleSearch();
            OnPropertyChanged(nameof(FilterLabel));
            UpdateOverlay();
            SyncActiveTab();
        }
    }

    public string FilterLabel => Filters.FilterList[Math.Clamp(FilterIndex, 0, Filters.FilterList.Length - 1)].Label;

    SortKey _sort = SortKey.Name;
    public SortKey Sort
    {
        get => _sort;
        set
        {
            if (!Set(ref _sort, value)) return;
            Ascending = value is SortKey.Name or SortKey.Path or SortKey.Extension;
            ScheduleSearch();
        }
    }

    bool _ascending = true;
    public bool Ascending
    {
        get => _ascending;
        set { if (Set(ref _ascending, value)) ScheduleSearch(); }
    }

    bool _showFilters = true;
    public bool ShowFilters { get => _showFilters; set => Set(ref _showFilters, value); }

    bool _statusBar = true;
    public bool StatusBarVisible
    {
        get => _statusBar;
        set { if (Set(ref _statusBar, value)) PersistUi(); }
    }

    bool _dark;
    public bool IsDark
    {
        get => _dark;
        set
        {
            if (!Set(ref _dark, value)) return;
            ThemeService.Apply(_dark, _indexOpts.Accent);
            PersistUi();
            OnPropertyChanged(nameof(ThemeGlyph));
            OnPropertyChanged(nameof(ThemeTooltip));
        }
    }

    bool _compactRows;
    public bool CompactRows
    {
        get => _compactRows;
        set
        {
            if (!Set(ref _compactRows, value)) return;
            OnPropertyChanged(nameof(ComfortableRows));
            OnPropertyChanged(nameof(ResultsRowHeight));
            PersistUi();
        }
    }

    public bool ComfortableRows
    {
        get => !_compactRows;
        set => CompactRows = !value;
    }

    public double ResultsRowHeight => _compactRows ? 34 : 48;

    public IndexOptions IndexOpts => _indexOpts;

    public bool PreferNtfs
    {
        get => _indexOpts.PreferNtfs;
        set { _indexOpts.PreferNtfs = value; _source.UpdateOptions(_indexOpts); PersistUi(); OnPropertyChanged(); }
    }

    public bool IndexRemovable
    {
        get => _indexOpts.IndexRemovable;
        set
        {
            _indexOpts.IndexRemovable = value;
            _source.UpdateOptions(_indexOpts);
            PersistUi();
            OnPropertyChanged();
            _source.RefreshRoots(_store, _progress, _cts.Token);
            RebuildLocations();
        }
    }

    public bool IndexNetwork
    {
        get => _indexOpts.IndexNetwork;
        set
        {
            _indexOpts.IndexNetwork = value;
            _source.UpdateOptions(_indexOpts);
            PersistUi();
            OnPropertyChanged();
            _source.RefreshRoots(_store, _progress, _cts.Token);
            RebuildLocations();
        }
    }

    public bool StartWithWindows
    {
        get => _indexOpts.StartWithWindows;
        set
        {
            _indexOpts.StartWithWindows = value;
            StartupManager.SetEnabled(value);
            PersistUi();
            OnPropertyChanged();
        }
    }

    public string AccentId
    {
        get => _indexOpts.Accent;
        set
        {
            if (string.Equals(_indexOpts.Accent, value, StringComparison.OrdinalIgnoreCase)) return;
            _indexOpts.Accent = value;
            ThemeService.Apply(_dark, _indexOpts.Accent);
            PersistUi();
            OnPropertyChanged();
        }
    }

    public bool IsAdmin => NtfsUsnScanner.IsAdmin();
    public string IndexModeLabel => $"Index: {_source.LastMode}";

    bool _showPalette;
    public bool ShowPalette
    {
        get => _showPalette;
        set
        {
            if (!Set(ref _showPalette, value)) return;
            if (_showPalette)
            {
                PaletteQuery = "";
                FilterPalette();
            }
        }
    }

    string _paletteQuery = "";
    public string PaletteQuery
    {
        get => _paletteQuery;
        set { if (Set(ref _paletteQuery, value)) FilterPalette(); }
    }

    public ObservableCollection<CommandItem> PaletteResults { get; } = [];
    List<CommandItem> _allCommands = [];
    int _paletteIndex;
    public int PaletteIndex
    {
        get => _paletteIndex;
        set => Set(ref _paletteIndex, value);
    }

    /// <summary>Segoe MDL2 Assets: sun when dark (switch to light), moon when light.</summary>
    public string ThemeGlyph => _dark ? "\uE706" : "\uE708";
    public string ThemeTooltip => _dark ? "Switch to light theme" : "Switch to dark theme";

    bool _showPreview = true;
    public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            if (!Set(ref _showPreview, value)) return;
            PersistUi();
            RefreshPreview();
            OnPropertyChanged(nameof(PreviewPaneVisible));
        }
    }

    public bool PreviewPaneVisible => _showPreview;

    public string MatchCountText => FormatUtil.Thousands((ulong)Math.Max(_matchTotal, 0));
    public string MatchCountLabel
    {
        get
        {
            if (HasActiveSearch())
                return _matchTotal == 1 ? "1 result" : $"{MatchCountText} results";
            return $"{FormatUtil.Thousands((ulong)Math.Max(_indexedCount, _matchTotal))} items";
        }
    }
    public bool IsIndexing => _progress.Running;
    public string IndexPathLabel
    {
        get
        {
            if (!_progress.Running) return "";
            var cur = _progress.Current;
            if (string.IsNullOrEmpty(cur)) return "Indexing…";
            if (cur.Length > 42) cur = cur[..42] + "…";
            return "Indexing " + cur;
        }
    }
    public string IndexFoundLabel =>
        _progress.Running ? $"{FormatUtil.Thousands((ulong)_progress.Found)} found" : "";
    /// <summary>0–1 visual progress while indexing (approximation for the status bar bar).</summary>
    public double IndexProgressFraction
    {
        get
        {
            if (!_progress.Running) return 0;
            var found = Math.Max(1.0, _progress.Found);
            // Soft asymptotic curve so the bar moves without a known total.
            return Math.Clamp(1.0 - 1.0 / Math.Sqrt(found / 50000.0 + 1.0), 0.08, 0.92);
        }
    }
    public string CopyPathLabel => _copiedPath ? "Copied" : "Copy path";
    bool _copiedPath;
    public bool HasSelection => SelectedIds.Count > 0 || CursorRow is >= 0 || Results.Count > 0;

    bool _showOverlay;
    public bool ShowOverlay { get => _showOverlay; private set => Set(ref _showOverlay, value); }

    string _overlayTitle = "";
    public string OverlayTitle { get => _overlayTitle; private set => Set(ref _overlayTitle, value); }

    string _overlayMessage = "";
    public string OverlayMessage { get => _overlayMessage; private set => Set(ref _overlayMessage, value); }

    bool _showHelp;
    public bool ShowHelp { get => _showHelp; set => Set(ref _showHelp, value); }

    bool _showAbout;
    public bool ShowAbout { get => _showAbout; set => Set(ref _showAbout, value); }

    string _windowTitle = AppConstants.AppName;
    public string WindowTitle { get => _windowTitle; private set => Set(ref _windowTitle, value); }

    string _statusLeft = "Starting...";
    public string StatusLeft { get => _statusLeft; private set => Set(ref _statusLeft, value); }

    string _statusRight = "";
    public string StatusRight { get => _statusRight; private set => Set(ref _statusRight, value); }

    string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public HashSet<uint> SelectedIds { get; } = [];
    public int? AnchorRow { get; set; }
    public int? CursorRow { get; set; }
    public bool ResultsFocus { get; set; }

    public ICommand ExitCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyFullNameCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand RebuildCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand RemoveBookmarkCommand { get; }
    public ICommand OpenHelpCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand OpenSelectedCommand { get; }
    public ICommand OpenPathCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand CopyToCommand { get; }
    public ICommand MoveToCommand { get; }
    public ICommand ZipSelectedCommand { get; }
    public ICommand BatchRenameCommand { get; }
    public ICommand ApplyBookmarkCommand { get; }
    public ICommand ApplyHistoryCommand { get; }
    public ICommand PinHistoryCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand QuickViewCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand SelectTabCommand { get; }
    public ICommand SelectLocationCommand { get; }
    public ICommand SetFilterCommand { get; } // parameter: filter index (int)
    public ICommand ClearQueryCommand { get; }
    public ICommand ClearRefineCommand { get; }
    public ICommand ClearSearchAndFiltersCommand { get; }
    public ICommand SetDensityCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand RunAsAdminCommand { get; }
    public ICommand TogglePaletteCommand { get; }

    bool CanAddBookmark => !string.IsNullOrWhiteSpace(Query) || FilterIndex != 0;

    public void ApplyStartOptions(StartOptions o)
    {
        Options.Case = o.Case ?? Options.Case;
        Options.Diacritics = o.Diacritics ?? Options.Diacritics;
        Options.Path = o.Path ?? Options.Path;
        Options.WholeWord = o.WholeWord ?? Options.WholeWord;
        Options.Regex = o.Regex ?? Options.Regex;
        NotifyOptions();

        if (o.Bookmark is { } bn)
        {
            var b = Bookmarks.FirstOrDefault(x => x.Name.Equals(bn, StringComparison.OrdinalIgnoreCase));
            if (b is not null) { Query = b.Query; FilterIndex = b.Filter; }
        }
        if (o.Filter is { } fn)
        {
            var i = Array.FindIndex(Filters.FilterList, f => f.Label.Equals(fn, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) FilterIndex = i;
        }
        if (o.Search is { } s) Query = s;
        if (o.Sort is { } sk)
        {
            _sort = sk;
            _ascending = sk is SortKey.Name or SortKey.Path or SortKey.Extension;
            OnPropertyChanged(nameof(Sort));
            OnPropertyChanged(nameof(Ascending));
        }
        if (o.Ascending is { } asc) Ascending = asc;
        ScheduleSearch();
    }

    public void ToggleOption(string name)
    {
        switch (name)
        {
            case "case": Options.Case = !Options.Case; break;
            case "ww": Options.WholeWord = !Options.WholeWord; break;
            case "path": Options.Path = !Options.Path; break;
            case "diacritics": Options.Diacritics = !Options.Diacritics; break;
            case "regex": Options.Regex = !Options.Regex; break;
        }
        NotifyOptions();
        ScheduleSearch();
    }

    void NotifyOptions()
    {
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(OptCase));
        OnPropertyChanged(nameof(OptWholeWord));
        OnPropertyChanged(nameof(OptPath));
        OnPropertyChanged(nameof(OptDiacritics));
        OnPropertyChanged(nameof(OptRegex));
    }

    public bool OptCase { get => Options.Case; set { Options.Case = value; NotifyOptions(); ScheduleSearch(); } }
    public bool OptWholeWord { get => Options.WholeWord; set { Options.WholeWord = value; NotifyOptions(); ScheduleSearch(); } }
    public bool OptPath { get => Options.Path; set { Options.Path = value; NotifyOptions(); ScheduleSearch(); } }
    public bool OptDiacritics { get => Options.Diacritics; set { Options.Diacritics = value; NotifyOptions(); ScheduleSearch(); } }
    public bool OptRegex { get => Options.Regex; set { Options.Regex = value; NotifyOptions(); ScheduleSearch(); } }

    public void ToggleSort(SortKey key)
    {
        if (Sort == key) Ascending = !Ascending;
        else Sort = key;
    }

    string SearchText()
    {
        var f = Filters.FilterList[Math.Clamp(FilterIndex, 0, Filters.FilterList.Length - 1)].Query;
        var q = Query;
        string core;
        if (string.IsNullOrEmpty(f)) core = q;
        else if (string.IsNullOrWhiteSpace(q)) core = f;
        else core = $"{f} <{q}>";

        if (!string.IsNullOrEmpty(_sizeChip))
            core = string.IsNullOrWhiteSpace(core) ? _sizeChip : $"{_sizeChip} <{core}>";
        if (!string.IsNullOrEmpty(_dateChip))
            core = string.IsNullOrWhiteSpace(core) ? _dateChip : $"{_dateChip} <{core}>";
        if (_contentSearch && !string.IsNullOrWhiteSpace(Query) &&
            !Query.Contains("content:", StringComparison.OrdinalIgnoreCase))
        {
            var contentTerm = $"content:{Query.Trim()}";
            core = string.IsNullOrWhiteSpace(core) ? contentTerm : $"{core} {contentTerm}";
        }

        if (string.IsNullOrEmpty(_locationRoot)) return core;
        var loc = $"path:{_locationRoot}\\";
        if (string.IsNullOrWhiteSpace(core)) return loc;
        return $"{loc} <{core}>";
    }

    void ScheduleSearch()
    {
        if (_debounce is null) return;
        // Idle / no filter: don't scan millions of rows — just show indexed count.
        if (!HasActiveSearch())
        {
            _debounce.Stop();
            _idleResearch.Stop();
            Results.Clear();
            _lastIds = [];
            _matchTotal = 0;
            _searching = false;
            ClearSelection();
            RefreshIndexedCount();
            UpdateOverlay();
            return;
        }
        _debounce.Stop();
        _debounce.Start();
    }

    void PumpSearch(bool force = false)
    {
        var text = SearchText();
        var meta = _store.SnapshotMeta();
        var debounceMs = meta.Ready ? 120 : 400;
        var optsChanged = Options.Case != _lastOpts.Case || Options.WholeWord != _lastOpts.WholeWord ||
                          Options.Path != _lastOpts.Path || Options.Diacritics != _lastOpts.Diacritics || Options.Regex != _lastOpts.Regex;
        var sameQuery = text == _lastSearchText && !optsChanged && Sort == _lastSort && Ascending == _lastAsc;
        var genChanged = meta.Generation != _lastSeenGeneration || meta.Epoch != _lastSeenEpoch;
        if (!force && sameQuery && !genChanged) return;
        if (!force && sameQuery && genChanged && (DateTime.UtcNow - _lastSent).TotalMilliseconds < debounceMs) return;

        _serial++;
        _lastSearchText = text;
        _lastOpts = Options.Clone();
        _lastSort = Sort;
        _lastAsc = Ascending;
        _lastSent = DateTime.UtcNow;
        _lastSeenGeneration = meta.Generation;
        _lastSeenEpoch = meta.Epoch;
        _searching = true;

        _search.Enqueue(new SearchRequest
        {
            Text = text,
            Options = Options.Clone(),
            Sort = Sort,
            Ascending = Ascending,
            Generation = meta.Generation,
            Epoch = meta.Epoch,
            Serial = _serial
        });
    }

    void OnReply(SearchReply reply) =>
        Application.Current?.Dispatcher.BeginInvoke(() => ApplyReply(reply), DispatcherPriority.Background);

    void ApplyReply(SearchReply reply)
    {
        if (reply.Serial < _shownSerial) return;
        _shownSerial = reply.Serial;
        Error = reply.Error;
        _searching = false;

        if (reply.Error is not null)
        {
            Results.Clear();
            _lastIds = [];
            _matchTotal = 0;
            UpdateStatusText();
            return;
        }

        var meta = _store.SnapshotMeta();
        if (meta.Epoch != reply.Epoch)
        {
            // Stale reply after index swap — search again against the live index.
            PumpSearch(force: true);
            UpdateStatusText();
            return;
        }

        _idsEpoch = reply.Epoch;
        _lastIds = reply.Ids;
        _matchTotal = reply.MatchTotal > 0 ? reply.MatchTotal : reply.Ids.Count;
        RebuildRows(reply.Ids);
        UpdateStatusText();
        RefreshPreview();
        RecordHistoryIfNeeded();
    }

    void RebuildRows(List<uint> ids)
    {
        // Cap the UI window; keep MatchTotal as the real hit count (shown in the search badge).
        var take = Math.Min(ids.Count, UiRowCap);
        var window = take == ids.Count ? ids : ids.GetRange(0, take);
        Results.Reset(window, _idsEpoch, _matchTotal);
    }

    bool HasActiveSearch() =>
        !string.IsNullOrWhiteSpace(_query) ||
        _filterIndex != 0 ||
        !string.IsNullOrEmpty(_sizeChip) ||
        !string.IsNullOrEmpty(_dateChip) ||
        !string.IsNullOrEmpty(_locationRoot) ||
        _contentSearch;

    void RefreshIndexedCount()
    {
        var meta = _store.SnapshotMeta();
        _indexedCount = meta.Count;
        // Empty catalog view: show count only — don't scan 3M rows into Results.
        if (!HasActiveSearch() && Results.Count == 0)
            _matchTotal = 0;
        UpdateStatusText();
    }

    void OnIndexChanged()
    {
        var meta = _store.SnapshotMeta();
        _indexedCount = meta.Count;
        var replaced = meta.Epoch != _lastSeenEpoch;
        var genChanged = meta.Generation != _lastSeenGeneration;
        if (!replaced && !genChanged)
        {
            UpdateStatusText();
            return;
        }

        // Full replace → search once. Watcher/enrich gen bumps → only if user is actively filtering.
        if (replaced || HasActiveSearch())
        {
            if (!_idleResearch.IsEnabled)
                _idleResearch.Start();
        }
        else
        {
            _lastSeenGeneration = meta.Generation;
            _lastSeenEpoch = meta.Epoch;
        }
        UpdateStatusText();
    }

    void OnProgressChanged()
    {
        if (_progress.Running)
            _indexedCount = Math.Max(_indexedCount, _progress.Found);
        else
            RefreshIndexedCount();
        UpdateStatusText();
    }

    void UpdateStatusText()
    {
        if (_statusMessage is not null && DateTime.UtcNow - _statusMessageAt < TimeSpan.FromSeconds(5))
            StatusLeft = _statusMessage;
        else if (Error is not null) StatusLeft = Error;
        else if (_indexedCount == 0 && _progress.Running)
            StatusLeft = "Indexing…";
        else if (_indexedCount == 0 && _searching)
            StatusLeft = "Loading index…";
        else if (SelectedIds.Count == 1) StatusLeft = "1 item selected";
        else if (SelectedIds.Count > 1) StatusLeft = $"{FormatUtil.Thousands((ulong)SelectedIds.Count)} items selected";
        else if (HasActiveSearch())
            StatusLeft = $"{FormatUtil.Thousands((ulong)_matchTotal)} of {FormatUtil.Thousands((ulong)_indexedCount)} items";
        else if (_matchTotal > Results.Count && Results.Count > 0)
            StatusLeft = $"{FormatUtil.Thousands((ulong)_matchTotal)} items (showing {FormatUtil.Thousands((ulong)Results.Count)})";
        else if (_indexedCount > 0 && _matchTotal == 0)
            StatusLeft = $"{FormatUtil.Thousands((ulong)_indexedCount)} items (showing {FormatUtil.Thousands((ulong)Math.Min(_indexedCount, UiRowCap))})";
        else if (_matchTotal == 1) StatusLeft = "1 item";
        else
            StatusLeft = $"{FormatUtil.Thousands((ulong)_matchTotal)} items";

        string right;
        if (_progress.Running)
            right = ""; // rendered by status-bar progress UI
        else if (_searching)
            right = _indexedCount > 0
                ? $"Searching {FormatUtil.Thousands((ulong)_indexedCount)} indexed items…"
                : "Loading index…";
        else if (_indexedCount > 0)
            right = $"{FormatUtil.Thousands((ulong)_indexedCount)} indexed  {_source.LastMode}";
        else
            right = "Waiting for index…";

        StatusRight = right;

        NotifyIfChanged(nameof(IndexModeLabel), IndexModeLabel, ref _lastIndexModeLabel);
        OnPropertyChanged(nameof(IsAdmin));
        var indexing = IsIndexing;
        if (indexing != _lastIsIndexing) { _lastIsIndexing = indexing; OnPropertyChanged(nameof(IsIndexing)); }
        NotifyIfChanged(nameof(IndexPathLabel), IndexPathLabel, ref _lastIndexPathLabel);
        NotifyIfChanged(nameof(IndexFoundLabel), IndexFoundLabel, ref _lastIndexFoundLabel);
        var frac = IndexProgressFraction;
        if (double.IsNaN(_lastIndexProgressFraction) || Math.Abs(frac - _lastIndexProgressFraction) > 0.0001)
        {
            _lastIndexProgressFraction = frac;
            OnPropertyChanged(nameof(IndexProgressFraction));
        }
        var hasSel = HasSelection;
        if (hasSel != _lastHasSelection) { _lastHasSelection = hasSel; OnPropertyChanged(nameof(HasSelection)); }
        UpdateTitle();
        NotifyIfChanged(nameof(MatchCountText), MatchCountText, ref _lastMatchCountText);
        NotifyIfChanged(nameof(MatchCountLabel), MatchCountLabel, ref _lastMatchCountLabel);
        UpdateOverlay();
        RefreshLocationCounts();
    }

    void NotifyIfChanged(string name, string value, ref string cache)
    {
        if (cache == value) return;
        cache = value;
        OnPropertyChanged(name);
    }

    void UpdateOverlay()
    {
        if (Results.Count > 0)
        {
            ShowOverlay = false;
            return;
        }

        if (_progress.Running && _indexedCount == 0)
        {
            OverlayTitle = "Indexing your drives";
            OverlayMessage = "QuickFind is building a fast local catalog. Search will light up as soon as the first pass finishes.";
            ShowOverlay = true;
            return;
        }

        if (_searching && _indexedCount == 0)
        {
            OverlayTitle = "Loading index";
            OverlayMessage = "Reading the cached catalog from disk…";
            ShowOverlay = true;
            return;
        }

        if (!_searching && _indexedCount > 0 && _matchTotal == 0)
        {
            if (Options.Regex && !string.IsNullOrWhiteSpace(Query))
            {
                OverlayTitle = "That regular expression isn't valid";
                OverlayMessage = "Check for an unclosed bracket or group, or turn off .* to search for the text literally.";
            }
            else if (!string.IsNullOrWhiteSpace(Query))
            {
                OverlayTitle = $"No matches for “{Query.Trim()}”";
                OverlayMessage = "Try fewer characters, or turn on Path to match folder names too.";
            }
            else
            {
                OverlayTitle = "Nothing matches these filters";
                OverlayMessage = "Try a wider size or date range, or another file type.";
            }
            ShowOverlay = true;
            return;
        }

        if (!_progress.Running && !_searching && _indexedCount == 0)
        {
            OverlayTitle = "No index yet";
            OverlayMessage = "Use Tools → Rebuild Index if scanning never starts.";
            ShowOverlay = true;
            return;
        }

        ShowOverlay = false;
    }

    void SyncFilterPills()
    {
        foreach (var p in FilterPills)
            p.IsSelected = p.Index == _filterIndex;
    }

    void PersistUi()
    {
        _persistUi.Stop();
        _persistUi.Start();
    }

    void PersistUiNow()
    {
        _persistUi.Stop();
        SyncActiveTab();
        var s = UiSettingsStore.Capture(_dark, _showPreview, _statusBar, Columns, _indexOpts, _compactRows);
        s.HotkeyEnabled = _hotkeyEnabled;
        s.CloseToTray = _closeToTray;
        s.Tabs = Tabs.Select(t => new TabSettings
        {
            Query = t.Query,
            Filter = t.FilterIndex,
            SizeChip = t.SizeChip,
            DateChip = t.DateChip,
            Location = t.LocationRoot,
            Sort = (int)t.Sort,
            Ascending = t.Ascending
        }).ToList();
        s.ActiveTab = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab));
        UiSettingsStore.Save(s);
    }

    void BuildPaletteCommands()
    {
        _allCommands =
        [
            new CommandItem { Title = "Toggle dark mode", Hint = "View", Run = () => IsDark = !IsDark },
            new CommandItem { Title = "Toggle preview pane", Hint = "View", Run = () => ShowPreview = !ShowPreview },
            new CommandItem { Title = "Rebuild index", Hint = "Tools", Run = () => RebuildCommand.Execute(null) },
            new CommandItem { Title = "Run as administrator", Hint = "Faster NTFS indexing", Run = () => Elevation.RelaunchAsAdmin() },
            new CommandItem { Title = "Export results", Hint = "File", Run = () => ExportCommand.Execute(null) },
            new CommandItem { Title = "Search syntax help", Hint = "Help", Run = () => ShowHelp = true },
            new CommandItem { Title = "Accent: Blue", Hint = "Theme", Run = () => AccentId = "blue" },
            new CommandItem { Title = "Accent: Slate", Hint = "Theme", Run = () => AccentId = "slate" },
            new CommandItem { Title = "Accent: Violet", Hint = "Theme", Run = () => AccentId = "violet" },
            new CommandItem { Title = "Accent: Orange", Hint = "Theme", Run = () => AccentId = "orange" },
            new CommandItem { Title = "Accent: Teal", Hint = "Theme", Run = () => AccentId = "teal" },
            new CommandItem { Title = "Toggle USB / removable indexing", Hint = "Indexing", Run = () => IndexRemovable = !IndexRemovable },
            new CommandItem { Title = "Toggle network drive indexing", Hint = "Indexing", Run = () => IndexNetwork = !IndexNetwork },
            new CommandItem { Title = "Toggle start with Windows", Hint = "Startup", Run = () => StartWithWindows = !StartWithWindows },
            new CommandItem { Title = "Toggle NTFS fast index preference", Hint = "Indexing", Run = () => PreferNtfs = !PreferNtfs },
            new CommandItem { Title = "Focus search", Hint = "Search", Run = () => FocusSearchRequested?.Invoke() },
            new CommandItem { Title = "Quick view: Biggest files", Hint = "View", Run = () => ApplyQuickView("biggest-files") },
            new CommandItem { Title = "Quick view: Biggest folders", Hint = "View", Run = () => ApplyQuickView("biggest-folders") },
            new CommandItem { Title = "Quick view: Duplicate names", Hint = "View", Run = () => ApplyQuickView("dupes") },
            new CommandItem { Title = "Quick view: Empty folders", Hint = "View", Run = () => ApplyQuickView("empty") },
            new CommandItem { Title = "Quick view: Changed today", Hint = "View", Run = () => ApplyQuickView("today") },
            new CommandItem { Title = "New search tab", Hint = "Tabs", Run = () => NewTab() },
            new CommandItem { Title = "Toggle content search", Hint = "Search", Run = () => ContentSearchEnabled = !ContentSearchEnabled },
            new CommandItem { Title = "Copy to…", Hint = "Files", Run = () => CopyTo() },
            new CommandItem { Title = "Move to…", Hint = "Files", Run = () => MoveTo() },
            new CommandItem { Title = "Zip selected…", Hint = "Files", Run = () => ZipSelected() },
            new CommandItem { Title = "Batch rename…", Hint = "Files", Run = () => BatchRename() },
        ];
    }

    void FilterPalette()
    {
        PaletteResults.Clear();
        var q = PaletteQuery.Trim();
        IEnumerable<CommandItem> src = _allCommands;
        if (!string.IsNullOrEmpty(q))
            src = _allCommands.Where(c =>
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Hint.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var c in src.Take(12))
            PaletteResults.Add(c);
        PaletteIndex = PaletteResults.Count > 0 ? 0 : -1;
    }

    public void PaletteMove(int delta)
    {
        if (PaletteResults.Count == 0) return;
        var next = PaletteIndex + delta;
        if (next < 0) next = PaletteResults.Count - 1;
        if (next >= PaletteResults.Count) next = 0;
        PaletteIndex = next;
    }

    public void ExecuteSelectedPalette()
    {
        if (PaletteIndex < 0 || PaletteIndex >= PaletteResults.Count) return;
        var cmd = PaletteResults[PaletteIndex];
        ShowPalette = false;
        cmd.Run();
    }

    public void SetAccent(string id) => AccentId = id;

    public void RefreshPreview()
    {
        if (_copiedPath)
        {
            _copiedPath = false;
            OnPropertyChanged(nameof(CopyPathLabel));
        }

        if (!_showPreview)
        {
            Preview.Clear();
            return;
        }

        uint? id = null;
        if (CursorRow is int c && c >= 0 && c < Results.Count)
            id = Results.IdAt(c);
        else if (SelectedIds.Count == 1)
            id = SelectedIds.First();

        if (id is null)
        {
            Preview.Clear();
            return;
        }

        var info = PreviewBuilder.Build(_store, id.Value);
        if (info is null) Preview.Clear();
        else Preview.Apply(info);
    }

    void UpdateTitle()
    {
        var q = Query.Trim();
        WindowTitle = string.IsNullOrEmpty(q) ? AppConstants.AppName : $"{q} - {AppConstants.AppName}";
    }

    void ClearSelection()
    {
        SelectedIds.Clear();
        AnchorRow = null;
        CursorRow = null;
        RefreshPreview();
    }

    public void SelectAll()
    {
        SelectedIds.Clear();
        for (var i = 0; i < Results.Count; i++)
            SelectedIds.Add(Results.IdAt(i));
        OnPropertyChanged(nameof(SelectedIds));
        UpdateStatusText();
    }

    public void CopySelectedFull()
    {
        var paths = SelectedRows().Select(r => r.FullPath);
        ShellService.CopyText(string.Join("\r\n", paths));
    }

    public void CopySelectedPath()
    {
        var rows = SelectedRows().ToList();
        if (rows.Count == 0) return;
        ShellService.CopyText(string.Join("\r\n", rows.Select(r => r.FullPath)));
        _copiedPath = true;
        OnPropertyChanged(nameof(CopyPathLabel));
        SetStatus("Copied path to clipboard");
    }

    public void OpenSelected()
    {
        foreach (var r in SelectedRows().Take(16)) ShellService.Open(r.FullPath);
    }

    public void OpenSelectedPath()
    {
        var row = SelectedRows().FirstOrDefault();
        if (row is not null) ShellService.OpenPath(row.FullPath);
    }

    IEnumerable<ResultRow> SelectedRows()
    {
        var rows = new List<ResultRow>();
        for (var i = 0; i < Results.Count; i++)
        {
            var id = Results.IdAt(i);
            if (!SelectedIds.Contains(id)) continue;
            if (Results.RowAt(i) is { } r) rows.Add(r);
        }
        if (rows.Count == 0 && CursorRow is int c && c >= 0 && c < Results.Count
            && Results.RowAt(c) is { } cur)
            rows.Add(cur);
        if (rows.Count == 0 && Results.Count > 0 && Results.RowAt(0) is { } first)
            rows.Add(first);
        return rows;
    }

    public void OpenRow(ResultRow row) => ShellService.Open(row.FullPath);

    public void ClickRow(int row, uint id, bool ctrl, bool shift)
    {
        ResultsFocus = true;
        if (shift && AnchorRow is int a)
        {
            SelectedIds.Clear();
            var lo = Math.Min(a, row);
            var hi = Math.Max(a, row);
            for (var i = lo; i <= hi && i < Results.Count; i++)
                SelectedIds.Add(Results.IdAt(i));
        }
        else if (ctrl)
        {
            if (!SelectedIds.Add(id)) SelectedIds.Remove(id);
            AnchorRow = row;
        }
        else
        {
            SelectedIds.Clear();
            SelectedIds.Add(id);
            AnchorRow = row;
        }
        CursorRow = row;
        OnPropertyChanged(nameof(SelectedIds));
        UpdateStatusText();
        RefreshPreview();
    }

    void AddBookmark()
    {
        BookmarkStore.Add(Bookmarks, Query, FilterIndex, FilterLabel);
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
    }

    void RemoveBookmark()
    {
        var i = Bookmarks.FindIndex(b => b.Query == Query && b.Filter == FilterIndex);
        if (i < 0) return;
        Bookmarks.RemoveAt(i);
        BookmarkStore.Save(Bookmarks);
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
    }

    void SelectLocation(string? root)
    {
        var normalized = string.IsNullOrWhiteSpace(root) ? null : root.TrimEnd('\\', '/');
        if (string.Equals(_locationRoot, normalized, StringComparison.OrdinalIgnoreCase)) return;
        _locationRoot = normalized;
        foreach (var loc in Locations)
            loc.IsSelected = string.Equals(loc.Root, _locationRoot, StringComparison.OrdinalIgnoreCase)
                             || (loc.Root is null && _locationRoot is null);
        ClearSelection();
        ScheduleSearch();
        SyncActiveTab();
    }

    public void OnRemovableDrivesChanged()
    {
        if (!_indexOpts.IndexRemovable) return;
        RebuildLocations();
        _source.RefreshRoots(_store, _progress, _cts.Token);
    }

    void RebuildLocations()
    {
        Locations.Clear();
        var all = new LocationNavItem("all", "All locations", null, "◫")
        {
            IsSelected = _locationRoot is null,
            CountText = _indexedCount > 0 ? FormatUtil.Thousands((ulong)_indexedCount) : ""
        };
        Locations.Add(all);

        foreach (var (root, kind) in DriveEnumerator.All())
        {
            if (kind is DriveKind.CdRom or DriveKind.Unknown) continue;
            var label = VolumeLabel(root, kind);
            var glyph = kind switch
            {
                DriveKind.Removable => "⏏",
                DriveKind.Remote => "☁",
                _ => "▣"
            };
            Locations.Add(new LocationNavItem(root, label, root.TrimEnd('\\'), glyph)
            {
                IsSelected = string.Equals(_locationRoot, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    static string VolumeLabel(string root, DriveKind kind)
    {
        try
        {
            var info = new DriveInfo(root);
            var name = string.IsNullOrWhiteSpace(info.VolumeLabel) ? null : info.VolumeLabel.Trim();
            var letter = root.TrimEnd('\\');
            if (name is not null) return $"{name} ({letter})";
            return kind switch
            {
                DriveKind.Removable => $"Removable ({letter})",
                DriveKind.Remote => $"Network ({letter})",
                _ => $"Local disk ({letter})"
            };
        }
        catch
        {
            return $"Disk ({root.TrimEnd('\\')})";
        }
    }

    void RefreshLocationCounts()
    {
        if (Locations.Count > 0)
            Locations[0].CountText = _indexedCount > 0 ? FormatUtil.Thousands((ulong)_indexedCount) : "";

        var meta = _store.SnapshotMeta();
        if (!meta.Ready || meta.Generation == _locationCountGeneration || _countingLocations) return;
        _countingLocations = true;
        _locationCountGeneration = meta.Generation;
        Task.Run(() =>
        {
            try
            {
                var map = new Dictionary<char, long>();
                _store.Read(idx =>
                {
                    foreach (var e in idx.Entries)
                    {
                        if (e.Deleted) continue;
                        var path = idx.DirPath(e);
                        if (path.Length >= 2 && path[1] == ':')
                        {
                            var c = char.ToUpperInvariant(path[0]);
                            map[c] = map.GetValueOrDefault(c) + 1;
                            continue;
                        }
                        var name = idx.Name(e);
                        if (name.Length >= 2 && name[1] == ':')
                        {
                            var c = char.ToUpperInvariant(name[0]);
                            map[c] = map.GetValueOrDefault(c) + 1;
                        }
                    }
                });

                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var loc in Locations)
                    {
                        if (loc.Root is null) continue;
                        var letter = char.ToUpperInvariant(loc.Root[0]);
                        loc.CountText = map.TryGetValue(letter, out var n) && n > 0
                            ? FormatUtil.Thousands((ulong)n)
                            : "";
                    }
                    _countingLocations = false;
                });
            }
            catch
            {
                _countingLocations = false;
            }
        });
    }

    void Export()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "results.efu",
            Filter = "File List (*.efu;*.csv)|*.efu;*.csv|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var lines = new System.Text.StringBuilder("Filename,Size,Date Modified,Date Created,Attributes\r\n");
        var count = 0;
        _store.Read(idx =>
        {
            if (idx.Epoch != _idsEpoch) return;
            foreach (var id in _lastIds)
            {
                if (id >= idx.Entries.Count) continue;
                var e = idx.Entries[(int)id];
                var size = e.IsDir || e.Size == AppConstants.NoSize ? "" : e.Size.ToString();
                var path = idx.FullPath(e).Replace("\"", "\"\"");
                lines.Append($"\"{path}\",{size},{e.Mtime},{e.Ctime},{e.Attrs}\r\n");
                count++;
            }
        });
        try
        {
            File.WriteAllText(dlg.FileName, lines.ToString());
            _statusMessage = $"Exported {FormatUtil.Thousands((ulong)count)} results";
            _statusMessageAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
            _statusMessageAt = DateTime.UtcNow;
        }
    }

    void RecordHistoryIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(Query) && FilterIndex == 0 && string.IsNullOrEmpty(_locationRoot)
            && string.IsNullOrEmpty(_sizeChip) && string.IsNullOrEmpty(_dateChip))
            return;
        SearchHistoryStore.Record(History, Query, FilterIndex, _locationRoot, FilterLabel);
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(HasHistory));
    }

    void ApplyHistory(HistoryEntry? e)
    {
        if (e is null) return;
        _sizeChip = "";
        _dateChip = "";
        OnPropertyChanged(nameof(SizeChip));
        OnPropertyChanged(nameof(DateChip));
        OnPropertyChanged(nameof(SizeChip10));
        OnPropertyChanged(nameof(SizeChip100));
        OnPropertyChanged(nameof(SizeChip1g));
        OnPropertyChanged(nameof(DateChipToday));
        OnPropertyChanged(nameof(DateChipWeek));
        OnPropertyChanged(nameof(DateChipMonth));
        Query = e.Query;
        FilterIndex = e.Filter;
        SelectLocation(e.Location);
        FocusSearchRequested?.Invoke();
    }

    void PinHistory(HistoryEntry? e)
    {
        if (e is null) return;
        var label = Filters.FilterList[Math.Clamp(e.Filter, 0, Filters.FilterList.Length - 1)].Label;
        BookmarkStore.Add(Bookmarks, e.Query, e.Filter, label);
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
        SetStatus($"Pinned “{e.Display}”");
    }

    void ClearHistory()
    {
        History.Clear();
        SearchHistoryStore.Save(History);
        OnPropertyChanged(nameof(History));
        OnPropertyChanged(nameof(HasHistory));
    }

    void ApplyQuickView(string? id)
    {
        switch (id)
        {
            case "biggest-files":
                FilterIndex = 0;
                SizeChip = "size:>10mb";
                DateChip = "";
                Query = "";
                _sort = SortKey.Size;
                _ascending = false;
                OnPropertyChanged(nameof(Sort));
                OnPropertyChanged(nameof(Ascending));
                ScheduleSearch();
                break;
            case "biggest-folders":
                FilterIndex = 5;
                SizeChip = "";
                DateChip = "";
                Query = "";
                _sort = SortKey.Size;
                _ascending = false;
                OnPropertyChanged(nameof(Sort));
                OnPropertyChanged(nameof(Ascending));
                ScheduleSearch();
                break;
            case "dupes":
                FilterIndex = 0;
                SizeChip = "";
                DateChip = "";
                Query = "dupe:";
                break;
            case "empty":
                FilterIndex = 5;
                SizeChip = "";
                DateChip = "";
                Query = "empty:";
                break;
            case "today":
                FilterIndex = 0;
                SizeChip = "";
                DateChip = "dm:today";
                Query = "";
                break;
        }
        FocusSearchRequested?.Invoke();
    }

    public void ApplyDroppedPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                SelectLocation(path.TrimEnd('\\', '/'));
                Query = "";
            }
            else if (File.Exists(path))
            {
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    SelectLocation(parent.TrimEnd('\\', '/'));
                Query = Path.GetFileName(path);
            }
            FocusSearchRequested?.Invoke();
        }
        catch { }
    }

    public string[] SelectedFullPaths() =>
        SelectedRows().Select(r => r.FullPath)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    void CopyTo() => TransferSelected(move: false);
    void MoveTo() => TransferSelected(move: true);

    void TransferSelected(bool move)
    {
        var paths = SelectedRows().Select(r => r.FullPath).ToArray();
        if (paths.Length == 0) return;
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = move ? "Move items to…" : "Copy items to…",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var dest = dlg.SelectedPath;
        Task.Run(() =>
        {
            var (ok, fail) = ShellService.CopyOrMove(paths, dest, move);
            Application.Current?.Dispatcher.BeginInvoke(() =>
                SetStatus(fail == 0
                    ? $"{(move ? "Moved" : "Copied")} {ok} item(s)"
                    : $"{(move ? "Moved" : "Copied")} {ok}, {fail} failed"));
        });
    }

    void ZipSelected()
    {
        var paths = SelectedRows().Select(r => r.FullPath).ToArray();
        if (paths.Length == 0) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "archive.zip",
            Filter = "Zip archive (*.zip)|*.zip"
        };
        if (dlg.ShowDialog() != true) return;
        var zipPath = dlg.FileName;
        Task.Run(() =>
        {
            try
            {
                ShellService.ZipPaths(paths, zipPath);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    SetStatus($"Created {Path.GetFileName(zipPath)}"));
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    SetStatus($"Zip failed: {ex.Message}"));
            }
        });
    }

    void BatchRename()
    {
        var rows = SelectedRows().ToList();
        if (rows.Count == 0) return;
        var owner = Application.Current?.MainWindow;
        var pattern = owner is null
            ? "{name}_{n}{ext}"
            : Views.InputDialog.Prompt(owner, "Batch rename",
                "Pattern tokens: {name}  {n}  {ext}\nExample: {name}_{n}{ext}", "{name}_{n}{ext}");
        if (string.IsNullOrWhiteSpace(pattern)) return;
        var n = 1;
        var ok = 0;
        var fail = 0;
        foreach (var row in rows)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(row.Name);
                var ext = Path.GetExtension(row.Name);
                var newName = pattern
                    .Replace("{name}", name, StringComparison.OrdinalIgnoreCase)
                    .Replace("{n}", n.ToString(), StringComparison.OrdinalIgnoreCase)
                    .Replace("{ext}", ext, StringComparison.OrdinalIgnoreCase);
                var dest = Path.Combine(row.Path, newName);
                if (string.Equals(dest, row.FullPath, StringComparison.OrdinalIgnoreCase)) { n++; continue; }
                if (row.IsDirectory) Directory.Move(row.FullPath, dest);
                else File.Move(row.FullPath, dest);
                ok++;
            }
            catch { fail++; }
            n++;
        }
        SetStatus(fail == 0 ? $"Renamed {ok} item(s)" : $"Renamed {ok}, {fail} failed");
    }

    void SetStatus(string msg)
    {
        _statusMessage = msg;
        _statusMessageAt = DateTime.UtcNow;
        UpdateStatusText();
    }

    void LoadTabs(UiSettings ui)
    {
        Tabs.Clear();
        if (ui.Tabs is { Count: > 0 })
        {
            foreach (var t in ui.Tabs.Take(6))
            {
                Tabs.Add(new SearchTabItem
                {
                    Query = t.Query ?? "",
                    FilterIndex = t.Filter,
                    SizeChip = t.SizeChip ?? "",
                    DateChip = t.DateChip ?? "",
                    LocationRoot = t.Location,
                    Sort = Enum.IsDefined(typeof(SortKey), t.Sort) ? (SortKey)t.Sort : SortKey.Name,
                    Ascending = t.Ascending
                });
            }
        }
        if (Tabs.Count == 0)
            Tabs.Add(new SearchTabItem());
        var idx = Math.Clamp(ui.ActiveTab, 0, Tabs.Count - 1);
        SelectTab(Tabs[idx], persist: false);
    }

    void NewTab()
    {
        if (Tabs.Count >= 6) { SetStatus("Maximum 6 tabs"); return; }
        SyncActiveTab();
        var tab = new SearchTabItem();
        Tabs.Add(tab);
        SelectTab(tab);
    }

    void CloseTab(SearchTabItem? tab)
    {
        if (tab is null || Tabs.Count <= 1) return;
        var i = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        SelectTab(Tabs[Math.Clamp(i - 1, 0, Tabs.Count - 1)]);
    }

    void SelectTab(SearchTabItem? tab, bool persist = true)
    {
        if (tab is null) return;
        SyncActiveTab();
        foreach (var t in Tabs) t.IsSelected = t == tab;
        ActiveTab = tab;
        _suppressTabSync = true;
        _sizeChip = tab.SizeChip ?? "";
        _dateChip = tab.DateChip ?? "";
        OnPropertyChanged(nameof(SizeChip));
        OnPropertyChanged(nameof(DateChip));
        OnPropertyChanged(nameof(SizeChip10));
        OnPropertyChanged(nameof(SizeChip100));
        OnPropertyChanged(nameof(SizeChip1g));
        OnPropertyChanged(nameof(DateChipToday));
        OnPropertyChanged(nameof(DateChipWeek));
        OnPropertyChanged(nameof(DateChipMonth));
        _query = tab.Query ?? "";
        OnPropertyChanged(nameof(Query));
        OnPropertyChanged(nameof(HasQuery));
        _filterIndex = tab.FilterIndex;
        OnPropertyChanged(nameof(FilterIndex));
        OnPropertyChanged(nameof(FilterLabel));
        SyncFilterPills();
        _sort = tab.Sort;
        _ascending = tab.Ascending;
        OnPropertyChanged(nameof(Sort));
        OnPropertyChanged(nameof(Ascending));
        SelectLocation(tab.LocationRoot);
        _suppressTabSync = false;
        UpdateTitle();
        ScheduleSearch();
        if (persist) PersistUi();
    }

    bool _suppressTabSync;

    void SyncActiveTab()
    {
        if (_suppressTabSync || ActiveTab is null) return;
        ActiveTab.Query = Query;
        ActiveTab.FilterIndex = FilterIndex;
        ActiveTab.SizeChip = SizeChip;
        ActiveTab.DateChip = DateChip;
        ActiveTab.LocationRoot = _locationRoot;
        ActiveTab.Sort = Sort;
        ActiveTab.Ascending = Ascending;
        ActiveTab.RefreshTitle();
    }

    public void RequestShowWindow() => ShowWindowRequested?.Invoke();

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        PersistUiNow();
        _cts.Cancel();
        _store.Changed -= _onIndexChanged;
        _progress.Changed -= _onProgressChanged;
        _debounce.Stop();
        _idleResearch.Stop();
        _persistUi.Stop();
        _search.Dispose();
        _source.Dispose();
    }
}