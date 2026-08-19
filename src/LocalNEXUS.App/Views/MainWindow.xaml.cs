using System.Windows;

namespace LocalNEXUS.App.Views;

/// <summary>
/// The application window. All behaviour lives in <see cref="ViewModels.MainViewModel"/>; this
/// file exists only because WPF requires a partial class for the generated markup.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
