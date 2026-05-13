namespace SnapRAIDGUI.Services;

using System.IO;
using System.Text;

public class LoggingService
{
    private readonly string _logDirectory;

    public LoggingService(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public void WriteLog(string operation, string content)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{operation}_{timestamp}.log";
        var filePath = Path.Combine(_logDirectory, fileName);

        var headerBuilder = new StringBuilder();
        headerBuilder.AppendLine($"=== SnapRAID GUI Log ===");
        headerBuilder.AppendLine($"Operation: {operation}");
        headerBuilder.AppendLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        headerBuilder.AppendLine($"========================================\n");
        headerBuilder.Append(content);

        File.WriteAllText(filePath, headerBuilder.ToString(), Encoding.UTF8);
    }

    public void AppendLog(string operation, string content)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{operation}_{timestamp}.log";
        var filePath = Path.Combine(_logDirectory, fileName);

        File.AppendAllText(filePath, content, Encoding.UTF8);
    }
}
