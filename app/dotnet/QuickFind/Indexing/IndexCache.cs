using System.IO;
using System.Text;
using QuickFind.Core;

namespace QuickFind.Indexing;

public static class IndexCache
{
    public static string AppDataDir
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, AppConstants.AppName);
        }
    }

    public static string CachePath => Path.Combine(AppDataDir, "index.bin");
    public static string BookmarksPath => Path.Combine(AppDataDir, "bookmarks.txt");

    public static void Save(FileIndex index)
    {
        Directory.CreateDirectory(AppDataDir);
        var tmp = CachePath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var w = new BinaryWriter(fs, Encoding.UTF8))
        {
            w.Write(AppConstants.IndexMagic);
            w.Write((uint)index.Dirs.Count);
            foreach (var d in index.Dirs)
                WriteStr(w, d.Path);

            var live = new List<Entry>(index.Entries.Count);
            foreach (var e in index.Entries)
                if (!e.Deleted) live.Add(e);
            live.Sort((a, b) =>
            {
                var cmp = index.FoldSpan(a).SequenceCompareTo(index.FoldSpan(b));
                return cmp != 0 ? cmp : index.NameSpan(a).SequenceCompareTo(index.NameSpan(b));
            });

            w.Write((uint)live.Count);
            foreach (var e in live)
            {
                w.Write(e.Dir);
                w.Write(e.SelfDir);
                w.Write(e.Size);
                w.Write(e.Mtime);
                w.Write(e.Ctime);
                w.Write(e.Attrs);
                w.Write((byte)0);
                WriteStr(w, index.Name(e));
            }
        }
        File.Move(tmp, CachePath, overwrite: true);
    }

    public static FileIndex? Load()
    {
        if (!File.Exists(CachePath)) return null;
        try
        {
            using var fs = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            using var r = new BinaryReader(fs, Encoding.UTF8);
            var magic = r.ReadBytes(8);
            if (!magic.AsSpan().SequenceEqual(AppConstants.IndexMagic)) return null;

            var index = new FileIndex();
            var nd = r.ReadUInt32();
            var paths = new List<string>((int)nd);
            for (var i = 0; i < nd; i++)
                paths.Add(ReadStr(r));

            foreach (var p in paths)
            {
                index.Dirs.Add(new Dir
                {
                    Path = p,
                    Fold = TextNorm.Fold(p),
                    Entry = AppConstants.None
                });
            }

            var ne = r.ReadUInt32();
            index.Entries.Capacity = (int)ne;
            for (var i = 0; i < ne; i++)
            {
                var dir = r.ReadUInt32();
                var selfDir = r.ReadUInt32();
                var size = r.ReadUInt64();
                var mtime = r.ReadUInt64();
                var ctime = r.ReadUInt64();
                var attrs = r.ReadUInt32();
                var del = r.ReadByte();
                var name = ReadStr(r);
                var e = Entry.Create(index.Names, name, dir, selfDir, size, mtime, ctime, attrs);
                e.Deleted = del != 0;
                index.Entries.Add(e);
            }
            index.FinalizeIndex();
            index.Ready = true;
            return index;
        }
        catch
        {
            return null;
        }
    }

    static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var n = Math.Min(b.Length, ushort.MaxValue);
        w.Write((ushort)n);
        w.Write(b, 0, n);
    }

    static string ReadStr(BinaryReader r)
    {
        var n = r.ReadUInt16();
        var b = r.ReadBytes(n);
        return Encoding.UTF8.GetString(b);
    }
}
