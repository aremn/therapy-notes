using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AudioRecorderTrayApp.Services
{
    public static class Logger
    {
        private static readonly object _lock = new object ();
        private static string RootDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRecorderTrayApp", "logs");

        public static string GetLogFilePath(DateTime? when = null)
        {
            var dt = (when ?? DateTime.Now).ToLocalTime();
            var name = $"app-{dt:yyyyMMdd}.log";
            Directory.CreateDirectory(RootDir);
            return Path.Combine(RootDir, name);
        }

        public static string? GetLatestLogPath()
        {
            if (!Directory.Exists(RootDir))
                return null;
            var files = Directory.GetFiles(RootDir, "app-*.log");
            if (files.Length == 0)
                return null;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files[^1];
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        private static void Write(string level, string message)
        {
            try
            {
                var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"{ts} [{level}] [T{Environment.CurrentManagedThreadId}] {message}";
                Debug.WriteLine(line);
                var path = GetLogFilePath();
                lock (_lock)
                {
                    var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                    File.AppendAllText(path, line + Environment.NewLine, enc);
                }
            }
            catch
            {
            }
        }

        public static string TailLastLines(int lines = 500)
        {
            var path = GetLatestLogPath();
            if (path == null || !File.Exists(path))
                return string.Empty;
            try
            {
                var all = File.ReadAllLines(path, Encoding.UTF8);
                if (all.Length <= lines)
                    return string.Join(Environment.NewLine, all);
                var slice = new string[lines];
                Array.Copy(all, all.Length - lines, slice, 0, lines);
                return string.Join(Environment.NewLine, slice);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
