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
        // Walk inner exceptions to find the real cause
        var sb = new System.Text.StringBuilder();
        var current = ex;
        int depth = 0;
        while (current != null && depth < 6)
        {
            sb.AppendLine(depth == 0 ? $"[{label}]" : $"[Inner {depth}]");
            sb.AppendLine($"{current.GetType().Name}: {current.Message}");
            sb.AppendLine();
            current = current.InnerException;
            depth++;
        }
        var msg = sb.ToString();

        try { _loggingService.WriteLog("crash", msg + "\n\n" + ex.StackTrace); } catch { }

        System.Windows.MessageBox.Show(
            msg,
            "Error — SnapRAID GUI",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }
}
