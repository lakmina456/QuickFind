using System.IO;
using System.Text.Json;
using QuickFind.Core;
using QuickFind.Indexing;

namespace QuickFind.Services;

public sealed class UiSettings
{
    public bool Dark { get; set; }
    public bool CompactRows { get; set; }
    public bool Preview { get; set; } = true;
    public bool StatusBar { get; set; } = true;
    public bool Path { get; set; } = true;
    public bool Size { get; set; } = true;
    public bool Extension { get; set; }
    public bool Modified { get; set; } = true;
    public bool Created { get; set; }
    public bool Attributes { get; set; }

    public bool PreferNtfs { get; set; } = true;
    public bool IndexFixed { get; set; } = true;
    public bool IndexRemovable { get; set; }
    public bool IndexNetwork { get; set; }
    public string Accent { get; set; } = "blue";
    public bool StartWithWindows { get; set; }
    public bool HotkeyEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public List<TabSettings>? Tabs { get; set; }
    public int ActiveTab { get; set; }
}

public sealed class TabSettings
{
    public string? Query { get; set; }
    public int Filter { get; set; }
    public string? SizeChip { get; set; }
    public string? DateChip { get; set; }
    public string? Location { get; set; }
    public int Sort { get; set; }
    public bool Ascending { get; set; } = true;
}

public static class UiSettingsStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public static string Path => System.IO.Path.Combine(IndexCache.AppDataDir, "ui.json");

    public static UiSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                // First run: follow Windows theme preference.
                var dark = false;
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var v = key?.GetValue("AppsUseLightTheme");
                    if (v is int i) dark = i == 0;
                }
                catch { }
                return new UiSettings { Dark = dark };
            }
            var s = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(Path), JsonOpts) ?? new UiSettings();
            // Prefer professional blue as default; migrate old green/teal defaults.
            if (string.IsNullOrWhiteSpace(s.Accent) ||
                s.Accent.Equals("green", StringComparison.OrdinalIgnoreCase) ||
                s.Accent.Equals("teal", StringComparison.OrdinalIgnoreCase))
                s.Accent = "blue";
            return s;
        }
        catch
        {
            return new UiSettings();
        }
    }

    public static void Save(UiSettings s)
    {
        try
        {
            Directory.CreateDirectory(IndexCache.AppDataDir);
            File.WriteAllText(Path, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch { }
    }

    public static void ApplyTo(ColumnVisibility cols, UiSettings s)
    {
        cols.Path = s.Path;
        cols.Size = s.Size;
        cols.Extension = s.Extension;
        cols.Modified = s.Modified;
        cols.Created = s.Created;
        cols.Attributes = s.Attributes;
    }

    public static IndexOptions ToIndexOptions(UiSettings s) => new()
    {
        PreferNtfs = s.PreferNtfs,
        IndexFixed = s.IndexFixed,
        IndexRemovable = s.IndexRemovable,
        IndexNetwork = s.IndexNetwork,
        Accent = s.Accent,
        StartWithWindows = s.StartWithWindows
    };

    public static UiSettings Capture(
        bool dark, bool preview, bool statusBar, ColumnVisibility cols, IndexOptions opts,
        bool compactRows = false) => new()
    {
        Dark = dark,
        CompactRows = compactRows,
        Preview = preview,
        StatusBar = statusBar,
        Path = cols.Path,
        Size = cols.Size,
        Extension = cols.Extension,
        Modified = cols.Modified,
        Created = cols.Created,
        Attributes = cols.Attributes,
        PreferNtfs = opts.PreferNtfs,
        IndexFixed = opts.IndexFixed,
        IndexRemovable = opts.IndexRemovable,
        IndexNetwork = opts.IndexNetwork,
        Accent = opts.Accent,
        StartWithWindows = opts.StartWithWindows
    };
}
