using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

/// <summary>
/// The tray icon for each state: the app icon plus a badge — grey connecting, red recording, white "…" busy
/// (loading, finishing, transcribing — the Mac's ellipsis), amber "!" error. Drawn once at startup; swapping NotifyIcon.Icon to a preloaded icon
/// creates no new handles.
/// </summary>
sealed class TrayIconSet : IDisposable
{
    readonly Dictionary<TrayIconKind, Icon> _icons = [];

    public TrayIconSet(Size size)
    {
        using var app = AppImage(size.Width);
        foreach (var kind in Enum.GetValues<TrayIconKind>())
        {
            using var bitmap = Compose(app, kind, size.Width);
            _icons[kind] = ToIcon(bitmap);
        }
    }

    public Icon this[TrayIconKind kind] => _icons[kind];

    /// <summary>The app icon at <paramref name="size"/> px, scaled from the embedded .ico's closest larger PNG frame.</summary>
    internal static Bitmap AppImage(int size)
    {
        using var stream = typeof(TrayIconSet).Assembly.GetManifestResourceStream("MyVoice.Windows.Assets.MyVoice.ico")!;
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(4);
        var count = reader.ReadUInt16();
        var frames = Enumerable.Range(0, count).Select(_ =>
        {
            var width = reader.ReadByte();
            reader.ReadBytes(7);
            var length = reader.ReadInt32();
            var offset = reader.ReadInt32();
            return (Size: width == 0 ? 256 : width, Length: length, Offset: offset);
        }).OrderBy(f => f.Size).ToList();
        var frame = frames.FirstOrDefault(f => f.Size >= size, frames[^1]);
        stream.Position = frame.Offset;
        using var source = new Bitmap(new MemoryStream(reader.ReadBytes(frame.Length))); // MyVoice.ico holds PNG frames
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, size, size);
        return image;
    }

    internal static Rectangle BadgeBounds(int size)
    {
        var diameter = (int)Math.Round(size * 0.56);
        return new Rectangle(size - diameter, size - diameter, diameter, diameter);
    }

    internal static Bitmap Compose(Bitmap app, TrayIconKind kind, int size)
    {
        // An exact copy: drawing the app image instead would re-blend its semi-transparent edge pixels.
        var bitmap = app.Clone(new Rectangle(0, 0, size, size), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Color? badge = kind switch
        {
            TrayIconKind.Connecting => Color.FromArgb(128, 132, 140),
            TrayIconKind.Recording => Color.FromArgb(229, 57, 53),
            TrayIconKind.Busy => Color.White, // on the blue app icon a coloured dot would vanish
            TrayIconKind.Error => Color.FromArgb(249, 168, 37),
            _ => null,
        };
        if (badge is null) return bitmap;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = BadgeBounds(size);
        var ring = Math.Max(1f, size / 16f); // a white edge keeps the badge readable on dark and light taskbars
        g.FillEllipse(Brushes.White, bounds);
        using (var fill = new SolidBrush(badge.Value))
            g.FillEllipse(fill, RectangleF.Inflate(bounds, -ring, -ring));
        if (kind == TrayIconKind.Busy)
        {
            var dot = Math.Max(1.5f, bounds.Width / 5.5f);
            var y = bounds.Top + (bounds.Height - dot) / 2f;
            using var dots = new SolidBrush(Color.FromArgb(30, 100, 200));
            foreach (var offset in new[] { -1.6f, 0f, 1.6f })
                g.FillEllipse(dots, bounds.Left + bounds.Width / 2f - dot / 2 + offset * dot, y, dot, dot);
        }
        if (kind == TrayIconKind.Error)
        {
            // "!" drawn as shapes: text is illegible at 16 px.
            var stroke = Math.Max(1.5f, bounds.Width / 6f);
            var centerX = bounds.Left + bounds.Width / 2f;
            using var mark = new SolidBrush(Color.FromArgb(40, 40, 40));
            g.FillRectangle(mark, centerX - stroke / 2, bounds.Top + bounds.Height * 0.2f, stroke, bounds.Height * 0.38f);
            g.FillRectangle(mark, centerX - stroke / 2, bounds.Top + bounds.Height * 0.66f, stroke, stroke);
        }
        return bitmap;
    }

    /// <summary>Wraps the bitmap as a one-frame PNG .ico, so the Icon owns its handle and keeps the alpha channel.</summary>
    static Icon ToIcon(Bitmap bitmap)
    {
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);          // ICONDIR: icon, one image
            writer.Write((byte)bitmap.Width); writer.Write((byte)bitmap.Height);             // ICONDIRENTRY
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)1); writer.Write((short)32);
            writer.Write((int)png.Length); writer.Write(6 + 16);
            writer.Write(png.ToArray());
        }
        ico.Position = 0;
        return new Icon(ico);
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
    }
}
