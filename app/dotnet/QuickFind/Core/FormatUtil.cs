namespace QuickFind.Core;

public static class FormatUtil
{
    public static string Thousands(ulong n)
    {
        var s = n.ToString();
        var outSb = new System.Text.StringBuilder(s.Length + s.Length / 3);
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) outSb.Append(',');
            outSb.Append(s[i]);
        }
        return outSb.ToString();
    }

    public static string SizeText(ulong size, bool isDir)
    {
        if (size == AppConstants.NoSize) return "";
        // Folders show rolled-up totals when available (0 still shown as 0 KB).
        if (isDir && size == 0) return "0 KB";
        return HumanBytes(size);
    }

    /// <summary>Compact size for table cells — avoids clipping (e.g. ",151,552 KB").</summary>
    public static string HumanBytes(ulong bytes)
    {
        if (bytes < 1024UL) return $"{bytes} B";
        double v = bytes;
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        var u = -1;
        do
        {
            v /= 1024.0;
            u++;
        } while (v >= 1024.0 && u < units.Length - 1);

        return v >= 100
            ? $"{v:0} {units[u]}"
            : v >= 10
                ? $"{v:0.0} {units[u]}"
                : $"{v:0.00} {units[u]}";
    }

    public static string AttribText(uint a)
    {
        var s = new System.Text.StringBuilder();
        foreach (var (bit, c) in new (uint, char)[] { (0x1, 'R'), (0x2, 'H'), (0x4, 'S'), (0x10, 'D'), (0x20, 'A'), (0x800, 'C'), (0x4000, 'E') })
        {
            if ((a & bit) != 0) s.Append(c);
        }
        return s.ToString();
    }
}
