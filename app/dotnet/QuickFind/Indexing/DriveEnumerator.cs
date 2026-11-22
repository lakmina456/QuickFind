using System.Runtime.InteropServices;

namespace QuickFind.Indexing;

public enum DriveKind
{
    Unknown,
    Removable,
    Fixed,
    Remote,
    CdRom,
    RamDisk
}

public static class DriveEnumerator
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetLogicalDrives();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetDriveTypeW(string lpRootPathName);

    public static List<(string Root, DriveKind Kind)> All()
    {
        var list = new List<(string, DriveKind)>();
        var mask = GetLogicalDrives();
        for (var i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            var root = $"{(char)('A' + i)}:";
            var kind = GetDriveTypeW(root + "\\") switch
            {
                2 => DriveKind.Removable,
                3 => DriveKind.Fixed,
                4 => DriveKind.Remote,
                5 => DriveKind.CdRom,
                6 => DriveKind.RamDisk,
                _ => DriveKind.Unknown
            };
            list.Add((root, kind));
        }
        return list;
    }

    public static List<string> SelectedRoots(bool fixedDrives, bool removable, bool network)
    {
        var roots = new List<string>();
        foreach (var (root, kind) in All())
        {
            var take = kind switch
            {
                DriveKind.Fixed => fixedDrives,
                DriveKind.Removable => removable,
                DriveKind.Remote => network,
                _ => false
            };
            if (take) roots.Add(root);
        }
        return roots;
    }
}
