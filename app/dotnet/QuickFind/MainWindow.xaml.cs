using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using QuickFind.Core;
using QuickFind.Services;
using QuickFind.ViewModels;

namespace QuickFind;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    TrayService? _tray;
    HotkeyService? _hotkey;
    DeviceChangeWatcher? _devices;
    bool _forceClose;
    Point? _dragStart;
    bool _preferSearchFocus;
    double _lastClipW, _lastClipH;
    const string GlyphMaximize = "\uE922";
    const string GlyphRestore = "\uE923";

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.FocusSearchRequested += () => RestoreSearchFocus(force: true);
        _vm.ShowWindowRequested += ShowFromTray;
        _vm.ExitAppRequested += () => { _forceClose = true; _tray?.Dispose(); Application.Current.Shutdown(); };
        _vm.HotkeyChanged += () => _hotkey?.SetEnabled(_vm.HotkeyEnabled);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowHelp) && _vm.ShowHelp)
            {
                new Views.HelpWindow { Owner = this }.ShowDialog();
                _vm.ShowHelp = false;
            }
            else if (e.PropertyName == nameof(MainViewModel.ShowAbout) && _vm.ShowAbout)
            {
                MessageBox.Show(this, $"{AppConstants.AppName}\nInstant file search for Windows.", AppConstants.AppName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _vm.ShowAbout = false;
            }
            else if (e.PropertyName == nameof(MainViewModel.ShowPalette) && _vm.ShowPalette)
                Dispatcher.BeginInvoke(() => { PaletteBox.Focus(); PaletteBox.SelectAll(); });
            else if (e.PropertyName == nameof(MainViewModel.DropHighlight))
                UpdateDropHighlight();
            else if (e.PropertyName is nameof(MainViewModel.MatchCountLabel)
                     or nameof(MainViewModel.StatusLeft)
                     or nameof(MainViewModel.ShowOverlay))
            {
                // Result/count updates can steal focus into the DataGrid — keep caret in the search box.
                if (_preferSearchFocus)
                    Dispatcher.BeginInvoke(() => RestoreSearchFocus(force: false), DispatcherPriority.Input);
            }
        };
        _vm.Results.CollectionChanged += (_, _) =>
        {
            if (_preferSearchFocus)
                Dispatcher.BeginInvoke(() => RestoreSearchFocus(force: false), DispatcherPriority.Input);
        };
        _vm.Columns.PropertyChanged += (_, _) => RefreshColumnVisibility();
        Loaded += (_, _) =>
        {
            var start = StartOptions.FromArgs(Environment.GetCommandLineArgs());
            _vm.ApplyStartOptions(start);
            RefreshColumnVisibility();
            RefreshMaxRestoreGlyph();
            ApplySearchCardClip();
            SearchBox.Focus();

            _tray = new TrayService(ShowFromTray, () => { _forceClose = true; Close(); Application.Current.Shutdown(); });
            _hotkey = new HotkeyService(this, ShowFromTray);
            _hotkey.Start();
            _hotkey.SetEnabled(_vm.HotkeyEnabled);

            _devices = new DeviceChangeWatcher(() =>
                Dispatcher.BeginInvoke(() => _vm.OnRemovableDrivesChanged()));
            _devices.Start(this);

            SingleInstance.StartListener(ShowFromTray, CancellationToken.None);
        };
        Closed += (_, _) =>
        {
            _devices?.Dispose();
            _hotkey?.Dispose();
            _tray?.Dispose();
            _vm.Dispose();
        };
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || !_vm.CloseToTray) return;
        e.Cancel = true;
        Hide();
    }

    void UpdateDropHighlight()
    {
        if (_vm.DropHighlight)
        {
            if (TryFindResource("AccentBrush") is Brush accent)
                SearchHost.BorderBrush = accent;
            return;
        }
        ApplySearchCardFocusVisual(SearchHost.IsKeyboardFocusWithin);
    }

    void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Link;
            _vm.DropHighlight = true;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_DragLeave(object sender, DragEventArgs e) => _vm.DropHighlight = false;

    void Window_Drop(object sender, DragEventArgs e)
    {
        _vm.DropHighlight = false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        _vm.ApplyDroppedPath(files[0]);
        e.Handled = true;
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Window_StateChanged(object sender, EventArgs e) => RefreshMaxRestoreGlyph();

    void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void RefreshMaxRestoreGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaxRestoreButton.Content = maximized ? GlyphRestore : GlyphMaximize;
        MaxRestoreButton.ToolTip = maximized ? "Restore" : "Maximize";

        if (WindowChrome.GetWindowChrome(this) is { } chrome)
            chrome.ResizeBorderThickness = maximized ? new Thickness(0) : new Thickness(6);
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.K)
        {
            _vm.ShowPalette = !_vm.ShowPalette;
            if (_vm.ShowPalette)
                Dispatcher.BeginInvoke(() => { PaletteBox.Focus(); PaletteBox.SelectAll(); });
            e.Handled = true;
            return;
        }

        if (_vm.ShowPalette)
        {
            if (e.Key == Key.Escape) { _vm.ShowPalette = false; e.Handled = true; }
            else if (e.Key == Key.Down) { _vm.PaletteMove(1); e.Handled = true; }
            else if (e.Key == Key.Up) { _vm.PaletteMove(-1); e.Handled = true; }
            else if (e.Key == Key.Enter) { _vm.ExecuteSelectedPalette(); e.Handled = true; }
            return;
        }

        if (ctrl && e.Key == Key.I) { _vm.ToggleOption("case"); e.Handled = true; }
        else if (ctrl && e.Key == Key.B) { _vm.ToggleOption("ww"); e.Handled = true; }
        else if (ctrl && e.Key == Key.U) { _vm.ToggleOption("path"); e.Handled = true; }
        else if (ctrl && e.Key == Key.M) { _vm.ToggleOption("diacritics"); e.Handled = true; }
        else if (ctrl && e.Key == Key.R) { _vm.ToggleOption("regex"); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { SearchBox.Focus(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { _vm.AddBookmarkCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.T) { _vm.NewTabCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.C && _vm.ResultsFocus) { _vm.CopyFullNameCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.A && _vm.ResultsFocus) { _vm.SelectAllCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter)
        {
            _vm.OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin)
        {
            if (_vm.Results.Count > 0)
            {
                _vm.ResultsFocus = true;
                _vm.ClickRow(0, _vm.Results.IdAt(0), false, false);
                ResultsGrid.SelectedIndex = 0;
                ResultsGrid.Focus();
                e.Handled = true;
            }
        }
        else if (_vm.ResultsFocus && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            MoveCursor(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    void MoveCursor(Key key, bool shift)
    {
        if (_vm.Results.Count == 0) return;
        var cur = _vm.CursorRow ?? 0;
        var page = Math.Max(1, (int)(ResultsGrid.ActualHeight / 28) - 1);
        var next = key switch
        {
            Key.Up => cur - 1,
            Key.Down => cur + 1,
            Key.PageUp => cur - page,
            Key.PageDown => cur + page,
            Key.Home => 0,
            Key.End => _vm.Results.Count - 1,
            _ => cur
        };
        if (next < 0)
        {
            SearchBox.Focus();
            _vm.ResultsFocus = false;
            return;
        }
        next = Math.Clamp(next, 0, _vm.Results.Count - 1);
        _vm.ClickRow(next, _vm.Results.IdAt(next), false, shift);
        ResultsGrid.SelectedIndex = next;
        if (_vm.Results.RowAt(next) is { } row)
            ResultsGrid.ScrollIntoView(row);
    }

    /// <summary>
    /// CSS overflow:hidden with border-radius:16px. WPF ClipToBounds is rectangular only.
    /// Clip the inner content host so the 1px rounded border on SearchHost stays crisp.
    /// </summary>
    void SearchHost_SizeChanged(object sender, SizeChangedEventArgs e) => ApplySearchCardClip();

    void ApplySearchCardClip()
    {
        var w = SearchHostContent.ActualWidth;
        var h = SearchHostContent.ActualHeight;
        if (w <= 0 || h <= 0) return;
        // Skip no-op clip updates (count text reflow) to avoid visual flicker.
        if (Math.Abs(w - _lastClipW) < 0.5 && Math.Abs(h - _lastClipH) < 0.5) return;
        _lastClipW = w;
        _lastClipH = h;
        const double r = 15;
        SearchHostContent.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }

    void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _preferSearchFocus = true;
        _vm.ResultsFocus = false;
        ApplySearchCardFocusVisual(true);
    }

    /// <summary>
    /// Drive the card ring from focus-within (HTML :focus-within), not from TextBox blur alone.
    /// Thickness never changes — only brush color — so the box cannot shift.
    /// </summary>
    void SearchHost_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var within = SearchHost.IsKeyboardFocusWithin || SearchHostOuter.IsKeyboardFocusWithin;
        if (!within)
            _preferSearchFocus = false;
        if (!_vm.DropHighlight)
            ApplySearchCardFocusVisual(within);
    }

    void ApplySearchCardFocusVisual(bool focused)
    {
        // Outer ring: always BorderThickness=4; transparent idle, accent-soft when focused.
        if (focused)
        {
            if (TryFindResource("AccentBrush") is Brush accent)
                SearchHost.BorderBrush = accent;
            if (TryFindResource("AccentSoftBrush") is Brush soft)
                SearchHostOuter.BorderBrush = soft;
        }
        else
        {
            if (TryFindResource("LineBrush") is Brush line)
                SearchHost.BorderBrush = line;
            SearchHostOuter.BorderBrush = Brushes.Transparent;
        }
    }

    void RestoreSearchFocus(bool force)
    {
        if (!force && !_preferSearchFocus) return;
        if (SearchBox.IsKeyboardFocusWithin) return;
        var start = SearchBox.SelectionStart;
        var len = SearchBox.SelectionLength;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        try { SearchBox.Select(start, len); } catch { /* ignore */ }
    }

    void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is ResultRow row) _vm.OpenRow(row);
    }

    void ResultsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ResultsGrid);
        if (ResultsGrid.InputHitTest(e.GetPosition(ResultsGrid)) is not DependencyObject hit) return;
        var row = ItemsControl.ContainerFromElement(ResultsGrid, hit) as DataGridRow;
        if (row?.Item is not ResultRow item) return;
        var index = ResultsGrid.Items.IndexOf(item);
        if (index < 0) return;
        _vm.ClickRow(index, item.Id,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    void ResultsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is null) return;
        var pos = e.GetPosition(ResultsGrid);
        if (Math.Abs(pos.X - _dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var paths = _vm.SelectedFullPaths();
        if (paths.Length == 0) return;
        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(ResultsGrid, data, DragDropEffects.Copy);
        _dragStart = null;
    }

    void ResultsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var key = e.Column.Header?.ToString() switch
        {
            "NAME" or "Name" => SortKey.Name,
            "PATH" or "Path" or "Location" => SortKey.Path,
            "SIZE" or "Size" => SortKey.Size,
            "EXT" or "Ext" or "Extension" => SortKey.Extension,
            "MODIFIED" or "Modified" or "Date Modified" => SortKey.Modified,
            "CREATED" or "Created" or "Date Created" => SortKey.Created,
            "ATTR" or "Attr" or "Attributes" => SortKey.Attributes,
            _ => SortKey.Name
        };
        _vm.ToggleSort(key);
    }

    void ColumnMenu_Click(object sender, RoutedEventArgs e) => RefreshColumnVisibility();

    void RefreshColumnVisibility()
    {
        ResultsGrid.Columns[1].Visibility = _vm.Columns.Path ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Columns[2].Visibility = _vm.Columns.Size ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Columns[3].Visibility = _vm.Columns.Extension ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Columns[4].Visibility = _vm.Columns.Modified ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Columns[5].Visibility = _vm.Columns.Created ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Columns[6].Visibility = _vm.Columns.Attributes ? Visibility.Visible : Visibility.Collapsed;
    }

    void FilterMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string t } && int.TryParse(t, out var i))
            _vm.FilterIndex = i;
    }

    void BookmarksMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (MenuItem)sender;
        while (menu.Items.Count > 2) menu.Items.RemoveAt(menu.Items.Count - 1);
        if (_vm.Bookmarks.Count == 0) return;
        menu.Items.Add(new Separator());
        foreach (var b in _vm.Bookmarks)
        {
            var item = new MenuItem { Header = b.Name, Tag = b };
            item.Click += (_, _) => _vm.ApplyBookmarkCommand.Execute(b);
            menu.Items.Add(item);
        }
    }

    void HistoryMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (MenuItem)sender;
        while (menu.Items.Count > 1) menu.Items.RemoveAt(menu.Items.Count - 1);
        if (_vm.History.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        menu.Items.Add(new Separator());
        foreach (var h in _vm.History.Take(30))
        {
            var item = new MenuItem { Header = h.Display };
            item.Click += (_, _) => _vm.ApplyHistoryCommand.Execute(h);
            var pin = new MenuItem { Header = "Pin", Tag = h };
            pin.Click += (_, _) => _vm.PinHistoryCommand.Execute(h);
            item.Items.Add(pin);
            menu.Items.Add(item);
        }
    }

    ResultRow? ContextRow() => ResultsGrid.SelectedItem as ResultRow
        ?? (_vm.CursorRow is int c && c >= 0 && c < _vm.Results.Count ? _vm.Results.RowAt(c) : null);

    void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow() is { } r) _vm.OpenRow(r);
    }

    void CtxOpenPath_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow() is { } r) ShellService.OpenPath(r.FullPath);
    }

    void CtxCopyFull_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow() is { } r) ShellService.CopyText(r.FullPath);
    }

    void CtxCopyName_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow() is { } r) ShellService.CopyText(r.Name);
    }

    void CtxCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ContextRow() is { } r) ShellService.CopyText(r.Path);
    }

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string id })
            _vm.SetAccent(id);
    }

    void IndexSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var menu = fe.ContextMenu ?? TryFindResource("IndexSettingsMenu") as ContextMenu;
        if (menu is null) return;
        menu.PlacementTarget = fe;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.DataContext = _vm;
        menu.IsOpen = true;
    }

    void PaletteScrim_MouseDown(object sender, MouseButtonEventArgs e) => _vm.ShowPalette = false;

    void PaletteList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _vm.ExecuteSelectedPalette();
}
