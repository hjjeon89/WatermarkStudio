using System.Windows;

namespace WatermarkStudio;

internal enum GestureKind { Move, Resize, Rotate }

internal sealed class LayerGesture(WatermarkLayer layer, Size imageSize, Point start, GestureKind kind, int corner = 0)
{
    private readonly WatermarkLayer before = layer.Copy();
    private readonly LayerLayout layout = WatermarkRenderer.Layout(layer, imageSize);
    private static readonly Point[] Corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];

    public void Update(Point point, bool snapRotation = false)
    {
        if (kind == GestureKind.Move)
        {
            Place(layout.Center + (point - start));
        }
        else if (kind == GestureKind.Rotate)
        {
            var angle = before.Rotation + Angle(point - layout.Center) - Angle(start - layout.Center);
            if (snapRotation) angle = Math.Round(angle / 15) * 15;
            layer.Rotation = ((angle + 180) % 360 + 360) % 360 - 180;
            Place(layout.Center);
        }
        else
        {
            var sign = Corners[corner];
            var handle = new Point(sign.X * layout.Size.Width / 2, sign.Y * layout.Size.Height / 2);
            var anchor = layout.ToImage(new Point(-handle.X, -handle.Y));
            var diagonal = layout.ToImage(handle) - anchor;
            var scale = Math.Max(.01, Vector.Multiply(point - anchor, diagonal) / Math.Max(.001, diagonal.LengthSquared));
            if (layer.IsText) layer.FontSize = before.FontSize * scale;
            else layer.Size = before.Size * scale;
            var resized = WatermarkRenderer.Layout(layer, imageSize);
            var offset = new System.Windows.Media.RotateTransform(before.Rotation).Transform(
                new Point(sign.X * resized.Size.Width / 2, sign.Y * resized.Size.Height / 2));
            Place(anchor + (Vector)offset);
        }
    }

    public void Cancel()
    {
        layer.FontSize = before.FontSize; layer.Size = before.Size;
        layer.Rotation = before.Rotation; layer.X = before.X; layer.Y = before.Y;
    }

    private void Place(Point center) { layer.X = center.X / imageSize.Width; layer.Y = center.Y / imageSize.Height; }
    private static double Angle(Vector vector) => Math.Atan2(vector.Y, vector.X) * 180 / Math.PI;
}
