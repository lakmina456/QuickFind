using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace QuickFind.Services;

public static class ShellService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    public static void Open(string path)
    {
        var target = path.EndsWith(':') ? path + "\\" : path;
        try { ShellExecuteW(IntPtr.Zero, "open", target, null, null, 1); }
        catch
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }
    }

    public static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public static void CopyText(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { }
    }

    public static (int Ok, int Fail) CopyOrMove(IReadOnlyList<string> paths, string destDir, bool move)
    {
        Directory.CreateDirectory(destDir);
        var ok = 0;
        var fail = 0;
        foreach (var src in paths)
        {
            try
            {
                var name = Path.GetFileName(src.TrimEnd('\\'));
                var dest = Path.Combine(destDir, name);
                if (Directory.Exists(src))
                {
                    if (move) Directory.Move(src, dest);
                    else CopyDirectory(src, dest);
                }
                else if (File.Exists(src))
                {
                    if (move) File.Move(src, dest, overwrite: true);
                    else File.Copy(src, dest, overwrite: true);
                }
                else { fail++; continue; }
                ok++;
            }
            catch { fail++; }
        }
        return (ok, fail);
    }

    static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    public static void ZipPaths(IReadOnlyList<string> paths, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                zip.CreateEntryFromFile(path, Path.GetFileName(path), CompressionLevel.Optimal);
            }
            else if (Directory.Exists(path))
            {
                var root = Path.GetFileName(path.TrimEnd('\\')) ?? "folder";
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.Combine(root, Path.GetRelativePath(path, file));
                    zip.CreateEntryFromFile(file, rel.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            }
        }
    }
}
