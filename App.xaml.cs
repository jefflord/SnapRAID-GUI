using System;
using System.Windows;
using SnapRAIDGUI.Views;
using SnapRAIDGUI.Services;

namespace SnapRAIDGUI;

public partial class App : Application
{
    private readonly LoggingService _loggingService = new("logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Global unhandled exception handlers — prevent crashes, show errors in console + log
        this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogAndShowError("UI Exception", e.Exception);
        e.Handled = true; // Prevent crash — let user see the error message
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        if (exception != null)
            LogAndShowError("AppDomain Exception", exception);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogAndShowError("Unobserved Task Exception", e.Exception);
        e.SetObserved(); // Prevent crash
    }

    private void LogAndShowError(string label, Exception ex)
    {
        var msg = $"[{label}]\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
        try { _loggingService.WriteLog("crash", msg); } catch { /* ignore logging failures */ }

        System.Windows.MessageBox.Show(
            $"{label}:\n\n{ex.Message}",
            "Error — SnapRAID GUI",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }
}
