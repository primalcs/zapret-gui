using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace zapret_gui;

public static class FlagIcons
{
    private static ImageSource? _us;
    private static ImageSource? _ru;

    public static ImageSource Us =>
        _us ??= CreateFlagImage(DrawUsFlag);

    public static ImageSource Ru =>
        _ru ??= CreateFlagImage(DrawRuFlag);

    public static ImageSource ForLanguage(AppLanguage language) =>
        language == AppLanguage.Ru ? Ru : Us;

    private static ImageSource CreateFlagImage(Action<DrawingContext, double, double> draw)
    {
        const double width = 22;
        const double height = 15;
        const int pixelWidth = 44;
        const int pixelHeight = 30;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(WpfBrushes.Transparent, null, new Rect(0, 0, width, height));
            draw(context, width, height);
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawRuFlag(DrawingContext context, double width, double height)
    {
        var bandHeight = height / 3;
        context.DrawRectangle(WpfBrushes.White, null, new Rect(0, 0, width, bandHeight));
        context.DrawRectangle(CreateBrush(0x00, 0x39, 0xA6), null, new Rect(0, bandHeight, width, bandHeight));
        context.DrawRectangle(CreateBrush(0xD5, 0x2B, 0x1E), null, new Rect(0, bandHeight * 2, width, bandHeight));
    }

    private static void DrawUsFlag(DrawingContext context, double width, double height)
    {
        const int stripeCount = 13;
        var stripeHeight = height / stripeCount;

        for (var i = 0; i < stripeCount; i++)
        {
            var brush = i % 2 == 0 ? CreateBrush(0xB2, 0x22, 0x34) : WpfBrushes.White;
            context.DrawRectangle(brush, null, new Rect(0, i * stripeHeight, width, stripeHeight));
        }

        var cantonWidth = width * 0.46;
        var cantonHeight = stripeHeight * 7;
        context.DrawRectangle(CreateBrush(0x3C, 0x3B, 0x6E), null, new Rect(0, 0, cantonWidth, cantonHeight));

        const int rows = 5;
        const int cols = 6;
        var starSize = cantonHeight / (rows * 2.4);
        var xStep = cantonWidth / (cols + 0.5);
        var yStep = cantonHeight / (rows + 0.5);

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var x = xStep * (col + 0.75);
                var y = yStep * (row + 0.75);
                context.DrawEllipse(
                    WpfBrushes.White,
                    null,
                    new WpfPoint(x, y),
                    starSize * 0.35,
                    starSize * 0.35);
            }
        }
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
