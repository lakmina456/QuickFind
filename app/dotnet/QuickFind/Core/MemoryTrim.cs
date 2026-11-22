using System.Runtime;

namespace QuickFind.Core;

/// <summary>Compact LOH after large index swaps — .NET otherwise keeps multi‑GB working sets.</summary>
public static class MemoryTrim
{
    public static void AfterLargeIndexChange()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        catch
        {
            try { GC.Collect(2, GCCollectionMode.Forced, blocking: false); }
            catch { /* ignore */ }
        }
    }
}
