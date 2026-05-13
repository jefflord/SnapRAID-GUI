using System.Windows;
using SnapRAIDGUI.Views;

namespace SnapRAIDGUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }
}
