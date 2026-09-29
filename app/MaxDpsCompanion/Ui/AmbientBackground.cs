using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MaxDpsCompanion;

/// <summary>
/// Fixed ambient backdrop for the shell: near-OLED ink, two very soft radial
/// glows (brass top-left, teal bottom-right) and a 3% film grain. Rendered
/// into a cached bitmap keyed by size and painted by the shell's own
/// background handler — deliberately NOT a child control, because
/// DrawToBitmap/WM_PRINT prints children front-to-back and a full-size child
/// would cover the entire page tree (the v2.8 snapshot regression). The layer
/// never scrolls (performance guardrail: no gradient work on content).
/// </summary>
internal static class AmbientBackdrop
{
    private static Bitmap? _cache;
    private static Bitmap? _grain;

    /// <summary>Paints the cached backdrop for the given size (rebuilds on resize).</summary>
    public static void Paint(Graphics graphics, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        if (_cache is null || _cache.Width != size.Width || _cache.Height != size.Height) Rebuild(size);
        if (_cache is not null) graphics.DrawImageUnscaled(_cache, 0, 0);
        else graphics.Clear(DesignTokens.Background);
    }

    private static void Rebuild(Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(DesignTokens.Background);
            DrawGlow(g, DesignTokens.GlowBrass,
                new Rectangle(-size.Width / 3, -size.Height / 3, size.Width, size.Height));
            DrawGlow(g, DesignTokens.GlowTeal,
                new Rectangle(size.Width / 3, size.Height / 3, size.Width, size.Height));
            using var texture = new TextureBrush(GrainTile());
            g.FillRectangle(texture, 0, 0, size.Width, size.Height);
        }
        _cache?.Dispose();
        _cache = bmp;
    }

    private static void DrawGlow(Graphics g, Color color, Rectangle bounds)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = color,
            SurroundColors = [Color.FromArgb(0, color)],
        };
        g.FillPath(brush, path);
    }

    private static Bitmap GrainTile()
    {
        if (_grain is not null) return _grain;
        const int size = 96;
        var tile = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        var rng = new Random(20260928);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var v = rng.Next(0, 255);
                var a = v < 128 ? 0 : 8; // sparse, 3% strength
                tile.SetPixel(x, y, Color.FromArgb(a, 255, 255, 255));
            }
        }
        _grain = tile;
        return tile;
    }
}
