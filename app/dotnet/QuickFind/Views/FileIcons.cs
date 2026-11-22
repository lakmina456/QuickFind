using System.Windows;
using System.Windows.Media;
using QuickFind.Core;

namespace QuickFind.Views;

/// <summary>Rust table::draw_icon — 16×16 folder / page with kind-colored band.</summary>
public static class FileIcons
{
    static readonly ImageSource Folder = Freeze(DrawFolder());
    static readonly ImageSource[] ByKind;

    static FileIcons()
    {
        ByKind = new ImageSource[Enum.GetValues<FileKind>().Length];
        foreach (FileKind k in Enum.GetValues<FileKind>())
            ByKind[(int)k] = Freeze(DrawFile(k));
    }

    public static ImageSource Get(bool isDirectory, FileKind kind) =>
        isDirectory ? Folder : ByKind[(int)kind];

    static ImageSource Freeze(DrawingImage img)
    {
        img.Freeze();
        return img;
    }

    static DrawingImage DrawFolder()
    {
        var g = new DrawingGroup();
        // tab
        g.Children.Add(new GeometryDrawing(
            Rgb(222, 170, 60), null,
            new RectangleGeometry(new Rect(1, 2, 6, 3), 1, 1)));
        // body
        g.Children.Add(new GeometryDrawing(
            Rgb(246, 205, 98), null,
            new RectangleGeometry(new Rect(1, 4, 14, 10), 1.5, 1.5)));
        // highlight line
        var line = new GeometryDrawing(
            null, new Pen(Rgb(255, 228, 150), 1),
            new LineGeometry(new Point(1, 6.5), new Point(15, 6.5)));
        g.Children.Add(line);
        return new DrawingImage(g);
    }

    static DrawingImage DrawFile(FileKind kind)
    {
        var g = new DrawingGroup();
        var page = new Rect(3, 1, 11, 14);
        g.Children.Add(new GeometryDrawing(
            Brushes.White,
            new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 1),
            new RectangleGeometry(page, 1, 1)));

        var band = kind switch
        {
            FileKind.Exe => Rgb(70, 120, 200),
            FileKind.Picture => Rgb(59, 130, 246),   // soft blue (no green)
            FileKind.Audio => Rgb(180, 100, 170),
            FileKind.Video => Rgb(130, 100, 200),
            FileKind.Archive => Rgb(190, 140, 60),
            FileKind.Document => Rgb(90, 140, 210),
            _ => null
        };

        if (band is not null)
        {
            // bottom band ~5px tall (Rust: left_bottom+1,-6 → right_bottom-1,1)
            g.Children.Add(new GeometryDrawing(
                band, null,
                new RectangleGeometry(new Rect(page.X + 1, page.Bottom - 6, page.Width - 2, 5))));
        }
        else
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 1);
            for (var k = 0; k < 3; k++)
            {
                var y = page.Top + 4 + k * 3;
                g.Children.Add(new GeometryDrawing(
                    null, pen,
                    new LineGeometry(new Point(page.Left + 2, y), new Point(page.Right - 2, y))));
            }
        }

        return new DrawingImage(g);
    }

    static SolidColorBrush Rgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
