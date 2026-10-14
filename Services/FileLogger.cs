using System;
using System.IO;

public static class FileLogger
{
    private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "update_log.txt");
    private static readonly object _lock = new object ();
    static FileLogger()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                File.Delete(LogFilePath);
            }
        }
        catch
        {
        }
    }

    public static void Info(string message)
    {
        Log("INFO", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        string fullMessage = message;
        if (ex != null)
        {
            fullMessage += Environment.NewLine + ex.ToString();
        }

        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                var enc = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                File.AppendAllText(LogFilePath, logEntry, enc);
            }
        }
        catch
        {
        }
    }
}
