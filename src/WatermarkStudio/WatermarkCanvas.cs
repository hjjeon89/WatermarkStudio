using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace WatermarkStudio;

public sealed class WatermarkCanvas : Canvas
{
    private EditorState? state;
    private LayerGesture? gesture;
    private WatermarkLayer? editingLayer;
    private string originalText = "";
    private readonly TextBox editor;
    private ImageSource? editingImage;
    private Rect imageRect;
    private double imageScale = 1;

    public EditorState? State
    {
        get => state;
        set
        {
            CommitTextEdit();
            if (state is not null) state.VisualChanged -= Changed;
            state = value;
            if (state is not null) state.VisualChanged += Changed;
            InvalidateVisual();
        }
    }

    public WatermarkCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Arrow;
        ToolTip = "끌어서 이동 · 모서리로 크기 조정 · 위쪽 원으로 회전 · 텍스트 두 번 클릭으로 편집";
        editor = new TextBox
        {
            Visibility = Visibility.Collapsed, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MaxLength = 500, Padding = new Thickness(5), BorderThickness = new Thickness(2),
            BorderBrush = Accent, Background = Brushes.White, Foreground = Brushes.Black,
            RenderTransformOrigin = new Point(.5, .5), VerticalContentAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(editor, "미리보기 텍스트 직접 편집");
        Children.Add(editor);
        editor.LostKeyboardFocus += (_, _) => CommitTextEdit();
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                CommitTextEdit(e.Key == Key.Escape);
                Focus();
                e.Handled = true;
            }
        };
        SizeChanged += (_, _) => { UpdateViewport(); UpdateEditor(); InvalidateVisual(); };
        LostMouseCapture += (_, _) => { gesture = null; Cursor = Cursors.Arrow; };
    }

    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(54, 112, 246));

    private void Changed(object? sender, EventArgs e)
    {
        if (editingLayer is not null && (state?.SelectedLayer != editingLayer || state.Image != editingImage || state.ShowOriginal || !state.CanEdit))
            CommitTextEdit();
        UpdateViewport();
        UpdateEditor();
        InvalidateVisual();
    }

    private void UpdateViewport()
    {
        if (state?.Image is not { } image || ActualWidth < 60 || ActualHeight < 60) { imageRect = Rect.Empty; return; }
        imageScale = Math.Min((ActualWidth - 56) / image.PixelWidth, (ActualHeight - 56) / image.PixelHeight);
        imageRect = new Rect((ActualWidth - image.PixelWidth * imageScale) / 2,
            (ActualHeight - image.PixelHeight * imageScale) / 2, image.PixelWidth * imageScale, image.PixelHeight * imageScale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (state?.Image is not { } image || ActualWidth < 60 || ActualHeight < 60) return;
        UpdateViewport();
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(221, 225, 232)), 1), imageRect);
        dc.PushTransform(new TranslateTransform(imageRect.X, imageRect.Y));
        dc.PushTransform(new ScaleTransform(imageScale, imageScale));
        if (state.ShowOriginal) dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        else
        {
            WatermarkRenderer.Draw(dc, image, state.Layers.Where(layer => layer != editingLayer));
            if (editingLayer is null && state.SelectedLayer is { } selected)
            {
                var layout = WatermarkRenderer.Layout(selected, new Size(image.PixelWidth, image.PixelHeight));
                dc.PushTransform(new TranslateTransform(layout.Center.X, layout.Center.Y));
                dc.PushTransform(new RotateTransform(layout.Rotation));
                var pen = new Pen(Accent, 1.5 / imageScale);
                dc.DrawRectangle(null, pen, layout.Bounds);
                var radius = 5 / imageScale;
                foreach (var corner in CornerPoints(layout))
                    dc.DrawRectangle(Brushes.White, pen, new Rect(corner.X - radius, corner.Y - radius, radius * 2, radius * 2));
                var top = new Point(0, -layout.Size.Height / 2);
                var rotation = RotationPoint(layout);
                dc.DrawLine(pen, top, rotation);
                dc.DrawEllipse(Brushes.White, pen, rotation, radius * 1.25, radius * 1.25);
                dc.Pop(); dc.Pop();
            }
        }
        dc.Pop(); dc.Pop();
    }

    private Point ImagePoint(Point point) => new((point.X - imageRect.X) / imageScale, (point.Y - imageRect.Y) / imageScale);
    private Point ScreenPoint(Point point) => new(point.X * imageScale + imageRect.X, point.Y * imageScale + imageRect.Y);
    private static Point[] CornerPoints(LayerLayout layout) =>
    [
        new(-layout.Size.Width / 2, -layout.Size.Height / 2), new(layout.Size.Width / 2, -layout.Size.Height / 2),
        new(layout.Size.Width / 2, layout.Size.Height / 2), new(-layout.Size.Width / 2, layout.Size.Height / 2)
    ];
    private Point RotationPoint(LayerLayout layout) => new(0, -layout.Size.Height / 2 - 28 / imageScale);

    private (GestureKind Kind, int Corner)? HitHandles(Point point)
    {
        if (state?.SelectedLayer is not { } selected || state.Image is not { } image) return null;
        var layout = WatermarkRenderer.Layout(selected, new Size(image.PixelWidth, image.PixelHeight));
        var local = layout.ToLocal(point);
        if ((local - RotationPoint(layout)).Length * imageScale <= 10) return (GestureKind.Rotate, 0);
        var corners = CornerPoints(layout);
        for (var i = 0; i < corners.Length; i++)
            if ((local - corners[i]).Length * imageScale <= 10) return (GestureKind.Resize, i);
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (editor.IsVisible && editor.IsMouseOver || state is not { CanEdit: true, ShowOriginal: false, Image: { } image }) return;
        CommitTextEdit();
        var point = ImagePoint(e.GetPosition(this));
        var size = new Size(image.PixelWidth, image.PixelHeight);
        var handle = HitHandles(point);
        var hit = handle is not null ? state.SelectedLayer :
            state.Layers.Reverse().FirstOrDefault(layer => WatermarkRenderer.Layout(layer, size).Contains(point));
        if (hit is null) return;
        state.SelectedLayer = hit;
        if (handle is null && e.ClickCount == 2 && hit.IsText)
        {
            BeginTextEdit();
            e.Handled = true;
            return;
        }
        gesture = new LayerGesture(hit, size, point, handle?.Kind ?? GestureKind.Move, handle?.Corner ?? 0);
        Cursor = handle?.Kind == GestureKind.Rotate ? Cursors.Hand : Cursors.SizeAll;
        Focus();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (state is not { CanEdit: true, ShowOriginal: false, Image: { } image } || editingLayer is not null) return;
        var point = ImagePoint(e.GetPosition(this));
        if (gesture is not null)
        {
            gesture.Update(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        var handle = HitHandles(point);
        if (handle?.Kind == GestureKind.Rotate) Cursor = Cursors.Hand;
        else if (handle?.Kind == GestureKind.Resize)
        {
            var angle = (state.SelectedLayer!.Rotation + (handle.Value.Corner % 2 == 0 ? 45 : -45) + 360) % 180;
            Cursor = ((int)Math.Round(angle / 45) % 4) switch
            { 0 => Cursors.SizeWE, 1 => Cursors.SizeNWSE, 2 => Cursors.SizeNS, _ => Cursors.SizeNESW };
        }
        else Cursor = state.Layers.Any(layer => WatermarkRenderer.Layout(layer, new Size(image.PixelWidth, image.PixelHeight)).Contains(point))
            ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F2 && editingLayer is null) { BeginTextEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape && gesture is not null)
        {
            gesture.Cancel();
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    internal void BeginTextEdit()
    {
        if (state is not { CanEdit: true, ShowOriginal: false, SelectedLayer: { IsText: true } layer, Image: { } image }) return;
        ReleaseMouseCapture();
        editingLayer = layer;
        editingImage = image;
        originalText = layer.Text;
        editor.SetBinding(TextBox.TextProperty, new Binding(nameof(WatermarkLayer.Text))
        { Source = layer, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        editor.Visibility = Visibility.Visible;
        UpdateEditor();
        editor.Focus();
        editor.SelectAll();
        state.Status = "텍스트 직접 편집 · Enter: 완료 · Shift+Enter: 줄바꿈 · Esc: 취소";
        InvalidateVisual();
    }

    internal void CommitTextEdit(bool cancel = false)
    {
        if (editingLayer is not { } layer) return;
        editingLayer = null;
        editingImage = null;
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        BindingOperations.ClearBinding(editor, TextBox.TextProperty);
        editor.Visibility = Visibility.Collapsed;
        if (cancel) layer.Text = originalText;
        if (state is not null) state.Status = "모서리로 크기 조정 · 위쪽 원으로 회전 · 텍스트 두 번 클릭으로 편집";
        InvalidateVisual();
    }

    private void UpdateEditor()
    {
        if (editingLayer is not { } layer || state?.Image is not { } image || imageRect.IsEmpty) return;
        var layout = WatermarkRenderer.Layout(layer, new Size(image.PixelWidth, image.PixelHeight));
        var center = ScreenPoint(layout.Center);
        editor.Width = Math.Max(120, layout.Size.Width * imageScale + 12);
        editor.Height = Math.Max(36, layout.Size.Height * imageScale + 8);
        editor.FontFamily = new FontFamily(layer.FontName);
        editor.FontSize = Math.Max(12, layer.FontSize * image.PixelWidth / 1000 * layout.Scale * imageScale);
        editor.FontWeight = layer.Bold ? FontWeights.Bold : FontWeights.Normal;
        editor.FontStyle = layer.Italic ? FontStyles.Italic : FontStyles.Normal;
        editor.RenderTransform = new RotateTransform(layer.Rotation);
        SetLeft(editor, center.X - editor.Width / 2);
        SetTop(editor, center.Y - editor.Height / 2);
    }
}
