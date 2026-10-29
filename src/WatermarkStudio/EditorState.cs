using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace WatermarkStudio;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }
    protected void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WatermarkLayer : Observable
{
    private string text = "© MY BRAND", fontName = "Malgun Gothic", colorHex = "#FFFFFF", backgroundHex = "#000000";
    private double fontSize = 56, size = 25, opacity = 70, rotation, x = .5, y = .5;
    private bool bold = true, italic, underline, shadow = true, background, tiled;

    public BitmapSource? Logo { get; init; }
    public string LogoName { get; init; } = "";
    public bool IsText => Logo is null;
    public string Label => IsText ? (string.IsNullOrWhiteSpace(Text) ? "빈 텍스트" : Text.ReplaceLineEndings(" ")) : LogoName;
    public string Text { get => text; set { Set(ref text, value); Notify(nameof(Label)); } }
    public string FontName { get => fontName; set => Set(ref fontName, value); }
    public double FontSize { get => fontSize; set => Set(ref fontSize, Math.Clamp(value, 12, 180)); }
    public double Size { get => size; set => Set(ref size, Math.Clamp(value, 3, 90)); }
    public double Opacity { get => opacity; set => Set(ref opacity, Math.Clamp(value, 0, 100)); }
    public double Rotation { get => rotation; set => Set(ref rotation, Math.Clamp(value, -180, 180)); }
    public double X { get => x; set => Set(ref x, Math.Clamp(value, 0, 1)); }
    public double Y { get => y; set => Set(ref y, Math.Clamp(value, 0, 1)); }
    public bool Bold { get => bold; set => Set(ref bold, value); }
    public bool Italic { get => italic; set => Set(ref italic, value); }
    public bool Underline { get => underline; set => Set(ref underline, value); }
    public bool Shadow { get => shadow; set => Set(ref shadow, value); }
    public bool Background { get => background; set => Set(ref background, value); }
    public bool Tiled { get => tiled; set => Set(ref tiled, value); }
    public string ColorHex { get => colorHex; set { ValidateColor(value); Set(ref colorHex, value); } }
    public string BackgroundHex { get => backgroundHex; set { ValidateColor(value); Set(ref backgroundHex, value); } }

    private static void ValidateColor(string value)
    {
        if (!Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("#FFFFFF 형식의 색상을 입력하세요.");
    }

    public WatermarkLayer Copy() => new()
    {
        Logo = Logo, LogoName = LogoName, Text = Text, FontName = FontName,
        FontSize = FontSize, Size = Size, Opacity = Opacity, Rotation = Rotation,
        X = X, Y = Y, Bold = Bold, Italic = Italic, Underline = Underline,
        Shadow = Shadow, Background = Background, Tiled = Tiled,
        ColorHex = ColorHex, BackgroundHex = BackgroundHex
    };
}

public sealed record ImageEntry(string Path, BitmapSource Thumbnail, int Width, int Height)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Dimensions => $"{Width:N0} × {Height:N0}";
}

public sealed class EditorState : Observable
{
    private ImageEntry? selectedImage;
    private WatermarkLayer? selectedLayer;
    private BitmapSource? image;
    private string status = "이미지를 선택하거나 여기에 끌어다 놓으세요.";
    private bool busy, showOriginal;
    private double progress;

    public ObservableCollection<ImageEntry> Images { get; } = [];
    public ObservableCollection<WatermarkLayer> Layers { get; } = [];
    public event EventHandler? VisualChanged;
    public BitmapSource? Image { get => image; private set { Set(ref image, value); Redraw(); } }
    public string Status { get => status; set => Set(ref status, value); }
    public double Progress { get => progress; set => Set(ref progress, value); }
    public bool Busy { get => busy; set { Set(ref busy, value); Notify(nameof(CanEdit)); Notify(nameof(CanExport)); } }
    public bool CanEdit => !Busy;
    public bool HasImages => Images.Count > 0;
    public bool HasLayer => SelectedLayer is not null;
    public bool CanExport => HasImages && Layers.Count > 0 && !Busy;
    public string ImageCount => $"이미지 {Images.Count}개";
    public bool ShowOriginal { get => showOriginal; set { Set(ref showOriginal, value); Redraw(); } }
    public WatermarkLayer? SelectedLayer
    {
        get => selectedLayer;
        set { Set(ref selectedLayer, value); Notify(nameof(HasLayer)); Redraw(); }
    }
    public ImageEntry? SelectedImage
    {
        get => selectedImage;
        set
        {
            if (selectedImage == value) return;
            try
            {
                var loaded = value is null ? null : ImageFiles.Load(value.Path);
                Set(ref selectedImage, value);
                Image = loaded;
                Status = value is null ? "이미지를 추가하세요." : $"{value.Name}  ·  {value.Dimensions} px";
            }
            catch (Exception ex) when (ImageFiles.IsImageError(ex))
            {
                Status = $"이미지를 열 수 없습니다: {ex.Message}";
                Notify();
            }
        }
    }

    public EditorState()
    {
        Images.CollectionChanged += (_, _) =>
        {
            Notify(nameof(HasImages)); Notify(nameof(ImageCount)); Notify(nameof(CanExport));
        };
    }

    public void AddLayer(WatermarkLayer layer)
    {
        layer.PropertyChanged += LayerChanged;
        Layers.Add(layer);
        SelectedLayer = layer;
        Notify(nameof(CanExport));
    }
    public void RemoveLayer()
    {
        if (SelectedLayer is not { } layer) return;
        layer.PropertyChanged -= LayerChanged;
        Layers.Remove(layer);
        SelectedLayer = Layers.LastOrDefault();
        Notify(nameof(CanExport));
    }
    private void LayerChanged(object? sender, PropertyChangedEventArgs e) => Redraw();
    private void Redraw() => VisualChanged?.Invoke(this, EventArgs.Empty);
}
