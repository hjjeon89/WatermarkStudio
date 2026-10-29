using System.ComponentModel;
using System.Windows;

namespace WatermarkStudio;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Window_Closing(object? sender, CancelEventArgs e) => Editor.OnWindowClosing(sender, e);
}
