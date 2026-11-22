using System.Windows;
using System.Windows.Media;

namespace QuickFind.Services;

public static class ThemeService
{
    // Default blue matches QuickFind.html tokens exactly (light / dark).
    public static readonly (string Id, string Label, Color Light, Color Dark, Color LightFill, Color DarkFill, Color LightInk, Color DarkInk, Color LightSoft, Color DarkSoft, Color LightOn, Color DarkOn)[] Accents =
    [
        ("blue", "Blue",
            Color.FromRgb(0x1F, 0x57, 0xD1), Color.FromRgb(0x7F, 0xA8, 0xFF),
            Color.FromRgb(0x2A, 0x62, 0xE0), Color.FromRgb(0x6B, 0x9B, 0xFF),
            Color.FromRgb(0x1A, 0x47, 0xB3), Color.FromRgb(0xA9, 0xC4, 0xFF),
            Color.FromRgb(0xE3, 0xEC, 0xFF), Color.FromRgb(0x1B, 0x2A, 0x4D),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x06, 0x12, 0x2B)),
        ("slate", "Slate",
            Color.FromRgb(0x47, 0x55, 0x69), Color.FromRgb(0x9A, 0xA4, 0xB2),
            Color.FromRgb(0x4A, 0x5A, 0x70), Color.FromRgb(0x8A, 0x96, 0xA8),
            Color.FromRgb(0x34, 0x40, 0x52), Color.FromRgb(0xC0, 0xC8, 0xD4),
            Color.FromRgb(0xE8, 0xEC, 0xF1), Color.FromRgb(0x2A, 0x32, 0x3E),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x0E, 0x12, 0x18)),
        ("violet", "Violet",
            Color.FromRgb(0x6D, 0x28, 0xD9), Color.FromRgb(0xA0, 0x8F, 0xD4),
            Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x9B, 0x8A, 0xE8),
            Color.FromRgb(0x5B, 0x21, 0xB6), Color.FromRgb(0xC4, 0xB5, 0xFD),
            Color.FromRgb(0xED, 0xE9, 0xFE), Color.FromRgb(0x2E, 0x1F, 0x4D),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x12, 0x0A, 0x28)),
        ("orange", "Orange",
            Color.FromRgb(0xC2, 0x41, 0x0C), Color.FromRgb(0xD4, 0x9A, 0x6A),
            Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xE0, 0xA8, 0x70),
            Color.FromRgb(0x9A, 0x34, 0x12), Color.FromRgb(0xF5, 0xD0, 0xA9),
            Color.FromRgb(0xFF, 0xED, 0xD5), Color.FromRgb(0x3D, 0x28, 0x14),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x1A, 0x0E, 0x06)),
        ("teal", "Teal",
            Color.FromRgb(0x0F, 0x76, 0x6E), Color.FromRgb(0x4A, 0x9B, 0x94),
            Color.FromRgb(0x14, 0x8A, 0x80), Color.FromRgb(0x5A, 0xB0, 0xA8),
            Color.FromRgb(0x0D, 0x5C, 0x56), Color.FromRgb(0x99, 0xE0, 0xD8),
            Color.FromRgb(0xCC, 0xF2, 0xEF), Color.FromRgb(0x16, 0x3A, 0x36),
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x06, 0x16, 0x14))
    ];

    public static void Apply(bool dark, string accentId = "blue")
    {
        var app = Application.Current;
        if (app is null) return;

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        ResourceDictionary? old = null;
        foreach (var d in app.Resources.MergedDictionaries)
        {
            if (d.Source is not null &&
                (d.Source.OriginalString.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                old = d;
                break;
            }
        }

        var next = new ResourceDictionary { Source = uri };
        if (old is not null)
        {
            var i = app.Resources.MergedDictionaries.IndexOf(old);
            app.Resources.MergedDictionaries[i] = next;
        }
        else
        {
            app.Resources.MergedDictionaries.Insert(0, next);
        }

        ApplyAccent(dark, accentId);
    }

    public static void ApplyAccent(bool dark, string accentId)
    {
        var app = Application.Current;
        if (app is null) return;
        var accent = Accents.FirstOrDefault(a => a.Id.Equals(accentId, StringComparison.OrdinalIgnoreCase));
        if (accent.Id is null) accent = Accents[0];

        var color = dark ? accent.Dark : accent.Light;
        var fill = dark ? accent.DarkFill : accent.LightFill;
        var ink = dark ? accent.DarkInk : accent.LightInk;
        var soft = dark ? accent.DarkSoft : accent.LightSoft;
        var on = dark ? accent.DarkOn : accent.LightOn;
        var sel = dark
            ? Color.FromRgb(0x19, 0x28, 0x4A)
            : Color.FromRgb(0xE6, 0xEE, 0xFF);
        // Non-blue accents: derive a soft selection tint from the accent.
        if (!accent.Id.Equals("blue", StringComparison.OrdinalIgnoreCase))
        {
            sel = dark
                ? Color.FromArgb(0xAA, soft.R, soft.G, soft.B)
                : Color.FromArgb(0xFF,
                    (byte)Math.Clamp((soft.R + 255) / 2, 0, 255),
                    (byte)Math.Clamp((soft.G + 255) / 2, 0, 255),
                    (byte)Math.Clamp((soft.B + 255) / 2, 0, 255));
        }

        SetBrush(app, "AccentColor", color);
        SetBrush(app, "AccentBrush", color);
        SetBrush(app, "AccentFillColor", fill);
        SetBrush(app, "AccentFillBrush", fill);
        SetBrush(app, "AccentSoftColor", soft);
        SetBrush(app, "AccentSoftBrush", soft);
        SetBrush(app, "AccentInkColor", ink);
        SetBrush(app, "AccentInkBrush", ink);
        SetBrush(app, "OnAccentColor", on);
        SetBrush(app, "OnAccentBrush", on);
        SetBrush(app, "SelColor", sel);
        SetBrush(app, "SelBrush", sel);
    }

    static void SetBrush(Application app, string key, Color color)
    {
        foreach (var d in app.Resources.MergedDictionaries)
        {
            if (!d.Contains(key)) continue;
            if (key.EndsWith("Brush", StringComparison.Ordinal))
            {
                var b = new SolidColorBrush(color);
                b.Freeze();
                d[key] = b;
            }
            else
            {
                d[key] = color;
            }
        }
    }
}
