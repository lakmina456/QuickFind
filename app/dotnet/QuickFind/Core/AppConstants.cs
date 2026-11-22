namespace QuickFind.Core;

public static class AppConstants
{
    public const string AppName = "QuickFind";
    public const uint None = uint.MaxValue;
    public const ulong NoSize = ulong.MaxValue;
    public const uint AttrDirectory = 0x10;
    public const uint AttrReparse = 0x400;
    public static readonly byte[] IndexMagic = "OFIX0001"u8.ToArray();
}
