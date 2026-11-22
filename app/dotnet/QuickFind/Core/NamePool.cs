namespace QuickFind.Core;

/// <summary>
/// Rust-style contiguous name pool. Lookups return ReadOnlySpan&lt;char&gt; (no alloc),
/// matching Index::name / Index::fold returning &amp;str.
/// </summary>
public sealed class NamePool
{
    char[] _buf = new char[4 * 1024 * 1024]; // 4M chars start; grows as needed
    int _len;

    public int Length => _len;

    public uint Append(ReadOnlySpan<char> s)
    {
        Ensure(_len + s.Length);
        s.CopyTo(_buf.AsSpan(_len));
        var off = (uint)_len;
        _len += s.Length;
        return off;
    }

    public uint Append(string s) => Append(s.AsSpan());

    public ReadOnlySpan<char> Slice(uint off, ushort len) =>
        _buf.AsSpan((int)off, len);

    public string GetString(uint off, ushort len) =>
        new(_buf, (int)off, len);

    public void AppendPool(NamePool other)
    {
        Ensure(_len + other._len);
        other._buf.AsSpan(0, other._len).CopyTo(_buf.AsSpan(_len));
        _len += other._len;
    }

    public void Clear() => _len = 0;

    public void TrimExcess()
    {
        if (_len <= 0) return;
        var need = Math.Max(_len, 1024);
        if (_buf.Length > need + need / 8)
            Array.Resize(ref _buf, need);
    }

    void Ensure(int need)
    {
        if (need <= _buf.Length) return;
        var n = _buf.Length;
        while (n < need) n *= 2;
        Array.Resize(ref _buf, n);
    }
}
