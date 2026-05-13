namespace SnapRAIDGUI.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using SnapRAIDGUI.ViewModels;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Width = 1100;
        Height = 750;
        MinWidth = 900;
        MinHeight = 600;
        WindowState = WindowState.Normal;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RefreshDashboard();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing dashboard:\n{ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TabSelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void ConsoleTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0 && sender is ScrollViewer sv)
            sv.ScrollToBottom();
    }

    private void ConsoleTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            if (sender is TextBox tb) tb.SelectAll();
            e.Handled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ShowSettingsDialog = false;
    }
}
