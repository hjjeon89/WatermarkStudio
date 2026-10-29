using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WatermarkStudio;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.GetFullPath(args.FirstOrDefault() ?? ".artifacts/test-check/fixtures");
        Directory.CreateDirectory(directory);
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine($"PASS {++checks}: {name}");
        }
        try
        {
            var white = Solid(200, 120, Colors.White);
            var red = Solid(40, 20, Colors.Red);
            var layer = new WatermarkLayer { Logo = red, Size = 50, Opacity = 50 };
            var rendered = WatermarkRenderer.Render(white, [layer]);
            var center = Pixel(rendered, 100, 60);
            Check(rendered.PixelWidth == 200 && rendered.PixelHeight == 120, "original pixel dimensions");
            Check(center.R == 255 && Math.Abs(center.G - 127) <= 1 && center.G == center.B, "logo alpha blends with original");
            Check(Pixel(rendered, 0, 0) == Colors.White && Pixel(white, 100, 60) == Colors.White, "corners and original remain unchanged");

            var transparent = Solid(200, 120, Colors.Transparent);
            var alpha = WatermarkRenderer.Render(transparent, [layer]);
            Check(Pixel(alpha, 0, 0).A == 0 && Math.Abs(Pixel(alpha, 100, 60).A - 128) <= 1, "PNG transparency preserved");
            Check(Pixel(WatermarkRenderer.Render(transparent, [], true), 0, 0) == Colors.White, "JPEG flattens transparency onto white");

            layer.Opacity = 100;
            layer.X = 1; layer.Y = 1; layer.Rotation = 45;
            var layout = WatermarkRenderer.Layout(layer, new Size(200, 120));
            var transform = new RotateTransform(layout.Rotation, layout.Center.X, layout.Center.Y);
            var bounds = new Rect(layout.Center.X - layout.Size.Width / 2, layout.Center.Y - layout.Size.Height / 2, layout.Size.Width, layout.Size.Height);
            var rotated = transform.TransformBounds(bounds);
            Check(rotated.Left >= 0 && rotated.Top >= 0 && rotated.Right <= 200 && rotated.Bottom <= 120, "rotated corner placement stays inside image");
            Check(layout.Contains(layout.Center) && !layout.Contains(new Point(-100, -100)), "rotated drag hit testing");

            layer.X = .5; layer.Y = .5; layer.Rotation = 0;
            var blue = new WatermarkLayer { Logo = Solid(40, 20, Colors.Blue), Size = 30, Opacity = 100 };
            Check(Pixel(WatermarkRenderer.Render(white, [layer, blue]), 100, 60) == Colors.Blue, "later layers render on top");
            layer.Tiled = true; layer.Size = 12; layer.Rotation = -30;
            var tiled = WatermarkRenderer.Render(white, [layer]);
            var tilePixels = Pixels(tiled);
            var redPixels = 0;
            for (var y = 0; y < 120; y += 2)
                for (var x = 0; x < 200; x += 2)
                    if (tilePixels[(y * tiled.PixelWidth + x) * 4 + 1] < 100) redPixels++;
            Check(redPixels > 250, "mosaic covers multiple regions after rotation");

            var text = new WatermarkLayer { Text = "한글 워터마크 ©", ColorHex = "#171B26", Shadow = false, FontSize = 100, Opacity = 100 };
            var textImage = WatermarkRenderer.Render(white, [text]);
            Check(!Pixels(textImage).SequenceEqual(Pixels(white)), "Korean text renders");
            var invalidColorRejected = false;
            try { text.ColorHex = "invalid"; } catch (ArgumentException) { invalidColorRejected = true; }
            Check(invalidColorRejected && text.ColorHex == "#171B26", "invalid colors are rejected without changing settings");

            var gestureImage = new Size(1000, 700);
            var adjustable = new WatermarkLayer { Text = "WATERMARK", FontSize = 50, Rotation = 25 };
            var initial = WatermarkRenderer.Layout(adjustable, gestureImage);
            var topLeft = initial.ToImage(new Point(-initial.Size.Width / 2, -initial.Size.Height / 2));
            var bottomRight = initial.ToImage(new Point(initial.Size.Width / 2, initial.Size.Height / 2));
            var resize = new LayerGesture(adjustable, gestureImage, bottomRight, GestureKind.Resize, 2);
            resize.Update(topLeft + (bottomRight - topLeft) * 1.3);
            var resized = WatermarkRenderer.Layout(adjustable, gestureImage);
            Check(Math.Abs(adjustable.FontSize - 65) < .001, "corner drag scales text font at any rotation");
            Check((resized.ToImage(new Point(-resized.Size.Width / 2, -resized.Size.Height / 2)) - topLeft).Length < .001,
                "resizing keeps the opposite rotated corner anchored");
            resize.Cancel();
            Check(adjustable.FontSize == 50 && adjustable.X == .5 && adjustable.Y == .5, "Escape restores the gesture snapshot");
            resize.Update(topLeft - (bottomRight - topLeft));
            Check(adjustable.FontSize == 12, "crossing resize anchor respects minimum size");
            resize.Cancel();
            adjustable.Rotation = 0;
            initial = WatermarkRenderer.Layout(adjustable, gestureImage);
            var rotateStart = initial.Center + new Vector(0, -100);
            var rotate = new LayerGesture(adjustable, gestureImage, rotateStart, GestureKind.Rotate);
            var radians = 37 * Math.PI / 180;
            var rotateEnd = initial.Center + new Vector(Math.Sin(radians) * 100, -Math.Cos(radians) * 100);
            rotate.Update(rotateEnd);
            Check(Math.Abs(adjustable.Rotation - 37) < .001, "rotation handle tracks pointer angle");
            rotate.Update(rotateEnd, true);
            Check(adjustable.Rotation == 30, "Shift snaps rotation to 15 degree steps");
            rotate.Cancel();
            adjustable.Rotation = 179;
            var wrapRotate = new LayerGesture(adjustable, gestureImage, rotateStart, GestureKind.Rotate);
            radians = 20 * Math.PI / 180;
            wrapRotate.Update(initial.Center + new Vector(Math.Sin(radians) * 100, -Math.Cos(radians) * 100));
            Check(Math.Abs(adjustable.Rotation + 161) < .001, "rotation passes through 180 degrees without sticking");
            adjustable.Rotation = 0;
            initial = WatermarkRenderer.Layout(adjustable, gestureImage);
            var move = new LayerGesture(adjustable, gestureImage, initial.Center, GestureKind.Move);
            move.Update(initial.Center + new Vector(50, 70));
            Check(Math.Abs(adjustable.X - .55) < .001 && Math.Abs(adjustable.Y - .6) < .001, "dragging moves in image coordinates");
            var logoResize = new LayerGesture(blue, gestureImage, new Point(650, 425), GestureKind.Resize, 2);
            logoResize.Update(new Point(800, 500));
            Check(Math.Abs(blue.Size - 45) < .001 && blue.FontSize == 56, "logo uses the same corner resize with aspect ratio preserved");
            logoResize.Cancel();

            var sentinel = Path.Combine(directory, "collision.png");
            File.WriteAllText(sentinel, "keep existing file");
            var saved = ImageFiles.Save(alpha, directory, "collision", ".png");
            var reloaded = ImageFiles.Load(saved);
            Check(saved != sentinel && File.ReadAllText(sentinel) == "keep existing file", "existing destination is never overwritten");
            Check(reloaded.PixelWidth == 200 && Pixel(reloaded, 0, 0).A == 0, "PNG save/load round trip");
            using (File.Open(saved, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Check(true, "loaded files are not left locked");
            var jpg = ImageFiles.Save(WatermarkRenderer.Render(transparent, [blue], true), directory, "sample", ".jpg");
            var gif = ImageFiles.Save(rendered, directory, "static", ".gif");
            Check(ImageFiles.Load(jpg).PixelWidth == 200 && ImageFiles.Load(gif).PixelHeight == 120, "JPEG and static GIF encoding");

            var orientedPath = Path.Combine(directory, "orientation6.jpg");
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var jpeg = new JpegBitmapEncoder();
            jpeg.Frames.Add(BitmapFrame.Create(white, null, metadata, null));
            using (var output = File.Create(orientedPath)) jpeg.Save(output);
            var oriented = ImageFiles.Load(orientedPath);
            var entry = ImageFiles.Inspect(orientedPath);
            Check(oriented.PixelWidth == 120 && oriented.PixelHeight == 200 && entry.Width == 120 && entry.Height == 200, "EXIF portrait orientation in source and thumbnail");

            var animatedPath = Path.Combine(directory, "animated.gif");
            var animated = new GifBitmapEncoder();
            animated.Frames.Add(BitmapFrame.Create(red));
            animated.Frames.Add(BitmapFrame.Create(Solid(40, 20, Colors.Blue)));
            using (var output = File.Create(animatedPath)) animated.Save(output);
            var animatedRejected = false;
            try { ImageFiles.Load(animatedPath); } catch (NotSupportedException) { animatedRejected = true; }
            Check(animatedRejected, "animated GIF explicitly rejected instead of losing frames");

            var corruptedPath = Path.Combine(directory, "corrupted.png");
            File.WriteAllText(corruptedPath, "not an image");
            var corruptedRejected = false;
            try { ImageFiles.Load(corruptedPath); } catch (Exception ex) when (ImageFiles.IsImageError(ex)) { corruptedRejected = true; }
            Check(corruptedRejected, "corrupt input is a recoverable image error");

            var export = ImageFiles.ExportAsync([saved, corruptedPath, jpg], [blue.Copy()], directory, "PNG",
                new InlineProgress(), CancellationToken.None).GetAwaiter().GetResult();
            Check(export.Saved == 2 && export.Errors.Count == 1 && !export.Cancelled, "batch export continues after a bad input");
            var stopped = ImageFiles.ExportAsync([saved], [blue.Copy()], directory, "PNG",
                new InlineProgress(), new CancellationToken(true)).GetAwaiter().GetResult();
            Check(stopped.Cancelled && stopped.Saved == 0, "cancellation stops the batch safely");

            var view = new EditorView();
            view.Measure(new Size(1320, 800));
            view.Arrange(new Rect(0, 0, 1320, 800));
            view.UpdateLayout();
            var state = (EditorState)view.DataContext;
            var fonts = ((System.Windows.Controls.ComboBox)view.FindName("FontPicker")).ItemsSource.Cast<string>();
            Check(fonts.Contains(new WatermarkLayer().FontName), "default font is selectable in installed font list");
            state.Images.Add(ImageFiles.Inspect(saved));
            state.SelectedImage = state.Images[0];
            state.AddLayer(text);
            view.UpdateLayout();
            Check(state.CanExport && state.HasLayer && state.Image is not null, "WPF landing/editor layout and export state");
            var canvas = (WatermarkCanvas)view.FindName("Preview");
            canvas.BeginTextEdit();
            var inline = canvas.Children.OfType<System.Windows.Controls.TextBox>().Single();
            inline.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "미리보기에서 직접 편집");
            inline.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            Check(text.Text == "미리보기에서 직접 편집", "inline editing updates the shared text layer");
            canvas.CommitTextEdit();
            Check(inline.Visibility == Visibility.Collapsed && text.Text == "미리보기에서 직접 편집", "inline commit retains edited text");
            canvas.BeginTextEdit();
            inline.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "취소할 내용");
            canvas.CommitTextEdit(true);
            Check(text.Text == "미리보기에서 직접 편집", "Escape cancels inline text changes");
            canvas.BeginTextEdit();
            state.ShowOriginal = true;
            Check(inline.Visibility == Visibility.Collapsed, "switching to original view closes inline editing");
            var window = new MainWindow();
            Check(window.Icon is { Width: > 0 }, "generated application icon loads from WPF resources");
            window.Close();
            var help = new HelpWindow();
            var helpContent = (FrameworkElement)help.Content;
            helpContent.Measure(new Size(740, 760));
            helpContent.Arrange(new Rect(0, 0, 740, 760));
            helpContent.UpdateLayout();
            Check(helpContent.ActualWidth > 0 && help.Icon is not null, "in-app help window loads and lays out");
            help.Close();
            state.RemoveLayer();
            Check(!state.CanExport && !state.HasLayer, "removing last watermark disables export");
            Console.WriteLine($"All {checks} checks passed. Fixtures: {directory}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private sealed class InlineProgress : IProgress<ExportProgress> { public void Report(ExportProgress value) { } }
    private static BitmapSource Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A; }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        return source;
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }
    private static Color Pixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
