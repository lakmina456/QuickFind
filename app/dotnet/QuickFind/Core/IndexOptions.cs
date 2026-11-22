namespace QuickFind.Core;

public sealed class IndexOptions
{
    /// <summary>Try NTFS USN/MFT enumeration (needs admin). Falls back to walk.</summary>
    public bool PreferNtfs { get; set; } = true;

    public bool IndexFixed { get; set; } = true;
    public bool IndexRemovable { get; set; }
    public bool IndexNetwork { get; set; }

    public string Accent { get; set; } = "blue";
    public bool StartWithWindows { get; set; }
}
