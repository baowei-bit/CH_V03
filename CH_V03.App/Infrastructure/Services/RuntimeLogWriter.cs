using System.IO;
using System.Text;

namespace CH_V03.App.Services;

/// <summary>
/// Writes the operator-facing runtime log to a daily UTF-8 file.
/// Disk failures are intentionally isolated from the test flow.
/// </summary>
public sealed class RuntimeLogWriter
{
    private readonly object _syncRoot = new();

    public void Write(string? directory, DateTime timestamp, string message)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            lock (_syncRoot)
            {
                Directory.CreateDirectory(directory);
                var filePath = Path.Combine(directory, $"{timestamp:yyyy-MM-dd}.log");
                File.AppendAllText(
                    filePath,
                    $"{timestamp:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Logging must never interrupt communication or production testing.
        }
    }
}
