using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WatermarkStudio;

public partial class EditorView : UserControl
{
    private readonly EditorState state = new();
    private CancellationTokenSource? cancellation;
    private bool closeAfterExport;

    public EditorView()
    {
        InitializeComponent();
        DataContext = state;
        Preview.State = state;
        FontPicker.ItemsSource = Fonts.SystemFontFamilies.Select(font => font.Source).OrderBy(name => name).ToArray();
    }

    public void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!state.Busy) return;
        e.Cancel = true;
        closeAfterExport = true;
        cancellation?.Cancel();
        state.Status = "현재 파일의 저장을 마치고 종료합니다…";
    }

    private void ShowHelp_Executed(object sender, ExecutedRoutedEventArgs e) =>
        new HelpWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void AddImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFiles.Filter, Multiselect = true, Title = "워터마크를 적용할 이미지 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddImages(dialog.FileNames);
    }

    private void AddImages(IEnumerable<string> paths)
    {
        if (state.Busy) return;
        var errors = new List<string>();
        var added = 0;
        foreach (var path in paths)
        {
            try
            {
                if (!ImageFiles.IsSupported(path)) { errors.Add($"{Path.GetFileName(path)}: JPG, PNG, 정적 GIF만 지원합니다."); continue; }
                var fullPath = Path.GetFullPath(path);
                if (state.Images.Any(image => string.Equals(image.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
                state.Images.Add(ImageFiles.Inspect(fullPath));
                added++;
            }
            catch (Exception ex) when (ImageFiles.IsImageError(ex)) { errors.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        if (state.SelectedImage is null) state.SelectedImage = state.Images.FirstOrDefault();
        state.Status = added > 0 ? $"이미지 {added}개 추가됨 · 텍스트 또는 로고를 추가하세요." : "추가할 새 이미지가 없습니다.";
        if (errors.Count > 0) ShowErrors("일부 이미지를 열 수 없습니다", errors);
    }

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = state.CanEdit ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Files_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        AddImages(files);
        e.Handled = true;
    }

    private void AddText_Click(object sender, RoutedEventArgs e)
    {
        state.ShowOriginal = false;
        state.AddLayer(new WatermarkLayer());
        state.Status = "텍스트를 편집하거나 미리보기에서 워터마크를 끌어 이동하세요.";
    }

    private void AddLogo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFiles.Filter, Title = "워터마크 로고 선택 (투명 PNG 권장)" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            state.AddLayer(new WatermarkLayer { Logo = ImageFiles.Load(dialog.FileName), LogoName = Path.GetFileName(dialog.FileName), Opacity = 80 });
            state.ShowOriginal = false;
        }
        catch (Exception ex) when (ImageFiles.IsImageError(ex)) { ShowErrors("로고를 열 수 없습니다", [ex.Message]); }
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e) => state.RemoveLayer();

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ImageEntry image }) return;
        var index = state.Images.IndexOf(image);
        if (state.SelectedImage == image) state.SelectedImage = null;
        state.Images.Remove(image);
        state.SelectedImage ??= state.Images.ElementAtOrDefault(Math.Min(index, state.Images.Count - 1));
        e.Handled = true;
    }

    private void Position_Click(object sender, RoutedEventArgs e)
    {
        if (state.SelectedLayer is not { } layer || sender is not Button { Tag: string tag }) return;
        var parts = tag.Split(',');
        layer.X = int.Parse(parts[0]) / 2d;
        layer.Y = int.Parse(parts[1]) / 2d;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (state.SelectedLayer is { } layer && sender is Button { Tag: string color }) layer.ColorHex = color;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!state.CanExport) return;
        Preview.CommitTextEdit();
        Keyboard.ClearFocus();
        if (HasValidationError(this))
        {
            MessageBox.Show(Window.GetWindow(this), "색상을 #FFFFFF 형식으로 입력하세요.", "색상 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (state.Layers.Any(layer => layer.IsText && string.IsNullOrWhiteSpace(layer.Text)))
        {
            MessageBox.Show(Window.GetWindow(this), "빈 텍스트 워터마크가 있습니다. 내용을 입력하거나 삭제하세요.", "텍스트 확인", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFolderDialog { Title = "워터마크 이미지를 저장할 폴더 선택" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var paths = state.Images.Select(image => image.Path).ToArray();
        var layers = state.Layers.Select(layer => layer.Copy()).ToArray();
        var format = (string)((ComboBoxItem)FormatPicker.SelectedItem).Tag;
        using var source = new CancellationTokenSource();
        cancellation = source;
        state.Busy = true;
        state.Progress = 0;
        state.Status = "워터마크 이미지를 저장하고 있습니다…";
        var progress = new Progress<ExportProgress>(value =>
        {
            state.Progress = value.Completed * 100d / value.Total;
            state.Status = $"{value.Completed}/{value.Total} 처리 · {value.FileName}";
        });
        try
        {
            var result = await ImageFiles.ExportAsync(paths, layers, dialog.FolderName, format, progress, source.Token);
            state.Status = $"{(result.Cancelled ? "작업 중지" : "저장 완료")} · {result.Saved}개 저장 · {dialog.FolderName}";
            if (!closeAfterExport)
            {
                var message = $"{result.Saved}개 이미지를 저장했습니다.\n\n{dialog.FolderName}";
                if (result.Cancelled) message = "작업을 중지했습니다. 이미 저장한 파일은 유지됩니다.\n\n" + message;
                if (result.Errors.Count > 0) message += $"\n\n실패 {result.Errors.Count}개:\n" + string.Join("\n", result.Errors.Take(8));
                MessageBox.Show(Window.GetWindow(this), message, result.Cancelled ? "작업 중지" : "저장 결과", MessageBoxButton.OK,
                    result.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            state.Status = $"저장에 실패했습니다: {ex.Message}";
            if (!closeAfterExport) ShowErrors("저장 실패", [ex.Message]);
        }
        finally
        {
            cancellation = null;
            state.Busy = false;
            if (closeAfterExport) Window.GetWindow(this)?.Close();
        }
    }

    private static bool HasValidationError(DependencyObject parent)
    {
        if (Validation.GetHasError(parent)) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (HasValidationError(VisualTreeHelper.GetChild(parent, i))) return true;
        return false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
        state.Status = "현재 파일의 저장을 마친 뒤 중지합니다…";
    }

    private void ShowErrors(string title, IEnumerable<string> errors) =>
        MessageBox.Show(Window.GetWindow(this), string.Join("\n", errors.Take(12)), title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
