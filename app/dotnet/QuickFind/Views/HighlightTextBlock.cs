using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickFind.Views;

/// <summary>TextBlock that wraps query matches in amber highlight runs (UI-only).</summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.Register(nameof(Query), typeof(string), typeof(HighlightTextBlock),
            new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty MatchCaseProperty =
        DependencyProperty.Register(nameof(MatchCase), typeof(bool), typeof(HighlightTextBlock),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty WholeWordProperty =
        DependencyProperty.Register(nameof(WholeWord), typeof(bool), typeof(HighlightTextBlock),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty RegexModeProperty =
        DependencyProperty.Register(nameof(RegexMode), typeof(bool), typeof(HighlightTextBlock),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty DiacriticsProperty =
        DependencyProperty.Register(nameof(Diacritics), typeof(bool), typeof(HighlightTextBlock),
            new PropertyMetadata(false, OnChanged));

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.Register(nameof(Enabled), typeof(bool), typeof(HighlightTextBlock),
            new PropertyMetadata(true, OnChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    public bool MatchCase
    {
        get => (bool)GetValue(MatchCaseProperty);
        set => SetValue(MatchCaseProperty, value);
    }

    public bool WholeWord
    {
        get => (bool)GetValue(WholeWordProperty);
        set => SetValue(WholeWordProperty, value);
    }

    public bool RegexMode
    {
        get => (bool)GetValue(RegexModeProperty);
        set => SetValue(RegexModeProperty, value);
    }

    public bool Diacritics
    {
        get => (bool)GetValue(DiacriticsProperty);
        set => SetValue(DiacriticsProperty, value);
    }

    public bool Enabled
    {
        get => (bool)GetValue(EnabledProperty);
        set => SetValue(EnabledProperty, value);
    }

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightTextBlock h) h.Rebuild();
    }

    void Rebuild()
    {
        var text = SourceText ?? "";
        if (string.IsNullOrEmpty(text))
        {
            Inlines.Clear();
            Text = "";
            return;
        }

        // Prefer Text (not Inlines) when there is nothing to highlight.
        // TextTrimming only measures correctly against Text; Inlines + CharacterEllipsis
        // inside a DataGrid cell often collapses to zero width (blank Name column).
        if (!Enabled || string.IsNullOrWhiteSpace(Query))
        {
            Inlines.Clear();
            Text = text;
            TextTrimming = TextTrimming.CharacterEllipsis;
            return;
        }

        var rx = BuildRx(Query.Trim(), MatchCase, WholeWord, RegexMode, Diacritics);
        if (rx is null)
        {
            Inlines.Clear();
            Text = text;
            TextTrimming = TextTrimming.CharacterEllipsis;
            return;
        }

        var fold = Fold(text, Diacritics);
        var matches = rx.Matches(fold);
        if (matches.Count == 0)
        {
            Inlines.Clear();
            Text = text;
            TextTrimming = TextTrimming.CharacterEllipsis;
            return;
        }

        // Highlight with Inlines — avoid TextTrimming (it zeros width with Inlines).
        TextTrimming = TextTrimming.None;
        Text = "";
        Inlines.Clear();
        var last = 0;
        foreach (Match m in matches)
        {
            if (m.Length == 0) continue;
            if (m.Index > last)
                Inlines.Add(new Run(text[last..m.Index]));
            Inlines.Add(new Run(text.Substring(m.Index, m.Length))
            {
                Background = TryFindResource("HlBrush") as Brush ?? Brushes.Gold,
                Foreground = TryFindResource("HlInkBrush") as Brush ?? Brushes.Black
            });
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            Inlines.Add(new Run(text[last..]));
        if (Inlines.Count == 0)
        {
            Text = text;
            TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    static Regex? BuildRx(string q, bool matchCase, bool wholeWord, bool regexMode, bool diacritics)
    {
        try
        {
            var p = regexMode ? q : Regex.Escape(q);
            if (wholeWord) p = @"\b(?:" + p + @")\b";
            p = Fold(p, diacritics);
            var opts = RegexOptions.CultureInvariant;
            if (!matchCase) opts |= RegexOptions.IgnoreCase;
            return new Regex(p, opts);
        }
        catch
        {
            return null;
        }
    }

    static string Fold(string s, bool keepDiacritics)
    {
        if (keepDiacritics || string.IsNullOrEmpty(s)) return s;
        var form = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(form.Length);
        foreach (var c in form)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
