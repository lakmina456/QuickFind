namespace QuickFind.Core;

public static class DateParser
{
    /// <summary>Windows FILETIME epoch; values at/near this are "unset" in USN-built indexes.</summary>
    const long MinValidFileTime = 116_444_736_000_000_000L; // 1970-01-01 UTC
    const long MaxSaneFileTime = 151_476_480_000_000_000L;   // ~year 2580+ buffer

    public static long FileTimeToUnix(ulong ft) => (long)(ft / 10_000_000) - 11_644_473_600L;

    public static bool IsValidFileTime(ulong ft) =>
        ft != 0 && (long)ft >= MinValidFileTime && (long)ft < MaxSaneFileTime;

    public static string FormatFileTime(ulong ft)
    {
        if (!IsValidFileTime(ft)) return "";
        try
        {
            var dt = DateTimeOffset.FromFileTime((long)ft).ToLocalTime();
            // Guard against FILETIME 0 leaking through as 01/01/1601 in local TZ.
            if (dt.Year < 1980) return "";
            return dt.ToString("dd/MM/yyyy HH:mm");
        }
        catch
        {
            return "";
        }
    }

    public static (long Lo, long Hi)? ParseRange(string arg)
    {
        var a = arg.Trim();
        if (a.StartsWith("<=", StringComparison.Ordinal))
            return Value(a[2..]) is { } v ? (long.MinValue, v.Hi) : null;
        if (a.StartsWith(">=", StringComparison.Ordinal))
            return Value(a[2..]) is { } v ? (v.Lo, long.MaxValue) : null;
        if (a.StartsWith('<'))
            return Value(a[1..]) is { } v ? (long.MinValue, v.Lo) : null;
        if (a.StartsWith('>'))
            return Value(a[1..]) is { } v ? (v.Hi, long.MaxValue) : null;
        if (a.StartsWith('='))
            return Value(a[1..]);
        var dots = a.IndexOf("..", StringComparison.Ordinal);
        if (dots >= 0)
        {
            if (Value(a[..dots]) is not { } l) return null;
            if (Value(a[(dots + 2)..]) is not { } r) return null;
            return (l.Lo, r.Hi);
        }
        return Value(a);
    }

    static (long Lo, long Hi)? Value(string v)
    {
        v = v.Trim().ToLowerInvariant();
        var now = DateTime.Now;
        var today = now.Date;
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = today.AddDays(-6);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var yearStart = new DateTime(today.Year, 1, 1);

        (long, long)? Period(string unit, int offset) => unit switch
        {
            "day" => (Day(today.AddDays(offset)), Day(today.AddDays(offset + 1))),
            "week" => (Day(weekStart.AddDays(7 * offset)), Day(weekStart.AddDays(7 * (offset + 1)))),
            "month" => (Day(AddMonths(monthStart, offset)), Day(AddMonths(monthStart, offset + 1))),
            "year" => (Day(AddMonths(yearStart, offset * 12)), Day(AddMonths(yearStart, offset * 12 + 12))),
            _ => null
        };

        if (v == "today") return Period("day", 0);
        if (v == "yesterday") return Period("day", -1);
        if (v == "tomorrow") return Period("day", 1);

        foreach (var (prefix, kind) in new[] { ("this", 0), ("last", -1), ("past", 2), ("next", 1) })
        {
            if (!v.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = v[prefix.Length..];
            var digits = new string(rest.TakeWhile(char.IsAsciiDigit).ToArray());
            var unit = rest[digits.Length..].TrimEnd('s');
            var n = long.TryParse(digits, out var parsed) ? parsed : 1;
            if (digits.Length == 0 && kind != 2)
            {
                if (Period(unit, kind) is { } r) return r;
            }

            var back = unit switch
            {
                "minute" => TimeSpan.FromMinutes(n),
                "hour" => TimeSpan.FromHours(n),
                "day" => TimeSpan.FromDays(n),
                "week" => TimeSpan.FromDays(7 * n),
                "month" => TimeSpan.FromDays(30 * n),
                "year" => TimeSpan.FromDays(365 * n),
                _ => (TimeSpan?)null
            };
            if (back is null) return null;
            var end = Ts(now);
            return kind == 1 ? (end, Ts(now + back.Value)) : (Ts(now - back.Value), end + 1);
        }

        var parts = v.Split('-', '/', '.');
        if (!parts.All(p => int.TryParse(p, out _))) return null;
        var nums = parts.Select(int.Parse).ToArray();
        return nums switch
        {
            [var y] when y > 1600 => (Day(new DateTime(y, 1, 1)), Day(AddMonths(new DateTime(y, 1, 1), 12))),
            [var y, var m] when y > 1600 => (Day(new DateTime(y, m, 1)), Day(AddMonths(new DateTime(y, m, 1), 1))),
            [var y, var m, var d] when y > 1600 => (Day(new DateTime(y, m, d)), Day(new DateTime(y, m, d).AddDays(1))),
            [var d, var m, var y] when y > 1600 => (Day(new DateTime(y, m, d)), Day(new DateTime(y, m, d).AddDays(1))),
            _ => null
        };
    }

    static long Day(DateTime d) => Ts(d.Date);
    static long Ts(DateTime d) => new DateTimeOffset(d).ToUnixTimeSeconds();
    static DateTime AddMonths(DateTime d, int m) => d.AddMonths(m);
}
