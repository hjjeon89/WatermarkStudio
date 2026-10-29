using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WatermarkStudio;

public sealed record LayerLayout(Drawing Content, Size Size, Point Center, double Scale, double Rotation)
{
    public Rect Bounds => new(-Size.Width / 2, -Size.Height / 2, Size.Width, Size.Height);
    public Point ToLocal(Point point) => new RotateTransform(-Rotation).Transform(point - (Vector)Center);
    public Point ToImage(Point point) => new RotateTransform(Rotation).Transform(point) + (Vector)Center;
    public bool Contains(Point point) => Bounds.Contains(ToLocal(point));
}

public static class WatermarkRenderer
{
    public static LayerLayout Layout(WatermarkLayer layer, Size imageSize)
    {
        var content = new DrawingGroup();
        Size size;
        using (var dc = content.Open())
        {
            if (layer.Logo is { } logo)
            {
                var width = imageSize.Width * layer.Size / 100;
                size = new Size(width, width * logo.PixelHeight / logo.PixelWidth);
                dc.DrawImage(logo, new Rect(size));
            }
            else
            {
                var fontSize = layer.FontSize * imageSize.Width / 1000;
                var text = new FormattedText(string.IsNullOrWhiteSpace(layer.Text) ? " " : layer.Text,
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(layer.FontName), layer.Italic ? FontStyles.Italic : FontStyles.Normal,
                        layer.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                    fontSize, Brush(layer.ColorHex), 1);
                text.MaxTextWidth = imageSize.Width * .9;
                if (layer.Underline) text.SetTextDecorations(TextDecorations.Underline);
                var padding = fontSize * .15;
                size = new Size(Math.Max(1, text.WidthIncludingTrailingWhitespace + padding * 2), text.Height + padding * 2);
                if (layer.Background) dc.DrawRoundedRectangle(Brush(layer.BackgroundHex), null, new Rect(size), padding, padding);
                if (layer.Shadow && !string.IsNullOrWhiteSpace(layer.Text))
                {
                    var geometry = text.BuildGeometry(new Point(padding + fontSize * .035, padding + fontSize * .035));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, geometry);
                }
                dc.DrawText(text, new Point(padding, padding));
            }
        }
        var radians = layer.Rotation * Math.PI / 180;
        var rotatedWidth = Math.Abs(size.Width * Math.Cos(radians)) + Math.Abs(size.Height * Math.Sin(radians));
        var rotatedHeight = Math.Abs(size.Width * Math.Sin(radians)) + Math.Abs(size.Height * Math.Cos(radians));
        var margin = Math.Min(imageSize.Width, imageSize.Height) * .025;
        var scale = Math.Min(1, Math.Min((imageSize.Width - margin * 2) / rotatedWidth,
            (imageSize.Height - margin * 2) / rotatedHeight));
        var halfWidth = rotatedWidth * scale / 2 + margin;
        var halfHeight = rotatedHeight * scale / 2 + margin;
        var center = new Point(
            Math.Clamp(layer.X * imageSize.Width, halfWidth, Math.Max(halfWidth, imageSize.Width - halfWidth)),
            Math.Clamp(layer.Y * imageSize.Height, halfHeight, Math.Max(halfHeight, imageSize.Height - halfHeight)));
        return new LayerLayout(content, new Size(size.Width * scale, size.Height * scale), center, scale, layer.Rotation);
    }

    public static void Draw(DrawingContext dc, BitmapSource source, IEnumerable<WatermarkLayer> layers)
    {
        var size = new Size(source.PixelWidth, source.PixelHeight);
        dc.PushClip(new RectangleGeometry(new Rect(size)));
        dc.DrawImage(source, new Rect(size));
        foreach (var layer in layers)
        {
            var layout = Layout(layer, size);
            dc.PushOpacity(layer.Opacity / 100);
            if (layer.Tiled)
            {
                var gap = Math.Min(size.Width, size.Height) * .06;
                var cell = new Rect(0, 0, layout.Size.Width + gap, layout.Size.Height + gap);
                var drawing = new DrawingGroup();
                using (var tile = drawing.Open())
                {
                    tile.PushTransform(new ScaleTransform(layout.Scale, layout.Scale));
                    tile.DrawDrawing(layout.Content);
                    tile.Pop();
                }
                var brush = new DrawingBrush(drawing)
                {
                    TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = cell,
                    Viewport = new Rect(layout.Center.X - layout.Size.Width / 2,
                        layout.Center.Y - layout.Size.Height / 2, cell.Width, cell.Height),
                    Stretch = Stretch.Fill,
                    Transform = new RotateTransform(layer.Rotation, layout.Center.X, layout.Center.Y)
                };
                dc.DrawRectangle(brush, null, new Rect(size));
            }
            else
            {
                dc.PushTransform(new TranslateTransform(layout.Center.X, layout.Center.Y));
                dc.PushTransform(new RotateTransform(layer.Rotation));
                dc.PushTransform(new TranslateTransform(-layout.Size.Width / 2, -layout.Size.Height / 2));
                dc.PushTransform(new ScaleTransform(layout.Scale, layout.Scale));
                dc.DrawDrawing(layout.Content);
                dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
            }
            dc.Pop();
        }
        dc.Pop();
    }

    public static BitmapSource Render(BitmapSource source, IEnumerable<WatermarkLayer> layers, bool whiteBackground = false)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (whiteBackground) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            Draw(dc, source, layers);
        }
        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
