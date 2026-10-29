using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WatermarkStudio;

public sealed record ExportProgress(int Completed, int Total, string FileName);
public sealed record ExportResult(int Saved, bool Cancelled, IReadOnlyList<string> Errors);

public static class ImageFiles
{
    public const string Filter = "이미지 파일 (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif";
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif";
    public static bool IsImageError(Exception ex) => ex is IOException or UnauthorizedAccessException or
        NotSupportedException or ArgumentException or FormatException or InvalidOperationException or COMException or System.Security.SecurityException;

    public static BitmapSource Load(string path, int thumbnailWidth = 0)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        // shortcut: reject animated GIFs; add frame composition and timing preservation before accepting them.
        if (decoder.Frames.Count != 1)
            throw new NotSupportedException("움직이는 GIF는 지원하지 않습니다. 정적 GIF, PNG 또는 JPG를 사용하세요.");
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 60_000_000 || frame.PixelWidth > 32767 || frame.PixelHeight > 32767)
            throw new NotSupportedException("이미지는 6,000만 픽셀 이하, 한 변 32,767px 이하로 선택하세요.");
        ushort orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try
            {
                if (metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value) orientation = value;
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or COMException) { }
        }
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        if (thumbnailWidth > 0) image.DecodePixelWidth = Math.Min(thumbnailWidth, frame.PixelWidth);
        image.EndInit();
        image.Freeze();
        var matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (matrix.IsIdentity) return image;
        var transformed = new TransformedBitmap(image, new MatrixTransform(matrix));
        transformed.Freeze();
        return transformed;
    }

    public static ImageEntry Inspect(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var thumbnail = Load(fullPath, 240);
        using var stream = File.OpenRead(fullPath);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames[0];
        var swapped = thumbnail.PixelWidth < thumbnail.PixelHeight && frame.PixelWidth > frame.PixelHeight ||
            thumbnail.PixelWidth > thumbnail.PixelHeight && frame.PixelWidth < frame.PixelHeight;
        return new ImageEntry(fullPath, thumbnail, swapped ? frame.PixelHeight : frame.PixelWidth, swapped ? frame.PixelWidth : frame.PixelHeight);
    }

    public static Task<ExportResult> ExportAsync(string[] paths, WatermarkLayer[] layers, string directory,
        string format, IProgress<ExportProgress> progress, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var saved = 0;
                var errors = new List<string>();
                for (var i = 0; i < paths.Length; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var path = paths[i];
                    try
                    {
                        var source = Load(path);
                        var extension = format switch
                        {
                            "PNG" => ".png", "JPG" => ".jpg",
                            _ => Path.GetExtension(path).ToLowerInvariant()
                        };
                        var result = WatermarkRenderer.Render(source, layers, extension is ".jpg" or ".jpeg");
                        Save(result, directory, Path.GetFileNameWithoutExtension(path) + "_watermarked", extension);
                        saved++;
                    }
                    catch (Exception ex) when (IsImageError(ex)) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
                    progress.Report(new ExportProgress(i + 1, paths.Length, Path.GetFileName(path)));
                }
                completion.SetResult(new ExportResult(saved, cancellationToken.IsCancellationRequested, errors));
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Watermark export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static string Save(BitmapSource image, string directory, string name, string extension)
    {
        BitmapEncoder encoder = extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".gif" => new GifBitmapEncoder(),
            ".png" => new PngBitmapEncoder(),
            _ => throw new NotSupportedException("지원하지 않는 출력 형식입니다.")
        };
        encoder.Frames.Add(BitmapFrame.Create(image));
        var temporary = Path.Combine(directory, $".watermark-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(output);
            for (var suffix = 0; ; suffix++)
            {
                var target = Path.Combine(directory, name + (suffix == 0 ? "" : $" ({suffix})") + extension);
                try { File.Move(temporary, target, false); return target; }
                catch (IOException) when (File.Exists(target)) { }
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
