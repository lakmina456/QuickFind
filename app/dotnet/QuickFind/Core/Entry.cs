namespace QuickFind.Core;

/// <summary>Rust Entry as a value type — flat list storage like Vec&lt;Entry&gt;.</summary>
public struct Entry
{
    public uint NameOff;
    public ushort NameLen;
    public ushort FoldLen;
    public uint FoldOff;
    public uint Dir;
    public uint SelfDir;
    public ulong Size;
    public ulong Mtime;
    public ulong Ctime;
    public uint Attrs;
    public uint Children;
    public bool Deleted;

    public readonly bool IsDir => SelfDir != AppConstants.None || (Attrs & AppConstants.AttrDirectory) != 0;

    public static Entry Create(NamePool names, ReadOnlySpan<char> name, uint dir, uint selfDir, ulong size, ulong mtime, ulong ctime, uint attrs)
    {
        if (name.Length > ushort.MaxValue)
            name = name[..ushort.MaxValue];

        var nameOff = names.Append(name);
        var (foldOff, foldLen) = TextNorm.AppendFolded(names, nameOff, (ushort)name.Length, name);

        return new Entry
        {
            NameOff = nameOff,
            NameLen = (ushort)name.Length,
            FoldOff = foldOff,
            FoldLen = foldLen,
            Dir = dir,
            SelfDir = selfDir,
            Size = size,
            Mtime = mtime,
            Ctime = ctime,
            Attrs = attrs
        };
    }

    public void Rebase(uint by)
    {
        NameOff += by;
        FoldOff += by;
    }
}
