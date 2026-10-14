using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AudioRecorderTrayApp.Core;

namespace AudioRecorderTrayApp.Services
{
    public static class PythonService
    {
        private static string GetPythonPath()
        {
            var configured = AppSettings.Current.PythonPath;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var candidates = Path.IsPathRooted(configured) ? new[]
                {
                    configured
                }

                : new[]
                {
                    Path.GetFullPath(configured),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured)
                };
                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            string embeddedPythonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python-embedded", "python.exe");
            if (File.Exists(embeddedPythonPath))
            {
                Logger.Info($"Using embedded Python: {embeddedPythonPath}");
                return embeddedPythonPath;
            }

            Logger.Info("Embedded Python not found, using system Python");
            return "python";
        }

        public static async Task<string> RunScriptAsync(string scriptPath, string arguments = "", string? input = null)
        {
            return await Task.Run(() => RunScript(scriptPath, arguments, input));
        }

        public static string RunScript(string scriptPath, string arguments = "", string? input = null)
        {
            var pythonPath = GetPythonPath();
            if (!Path.IsPathRooted(scriptPath))
            {
                scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, scriptPath);
            }

            string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Logger.Info($"Using application directory as working directory: {workingDirectory}");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" {arguments}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = !string.IsNullOrEmpty(input),
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workingDirectory
                }
            };
            process.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            process.StartInfo.Environment["PYTHONUTF8"] = "1";
            process.StartInfo.Environment["NATASHA_VERBOSE"] = AppSettings.Current.VerboseTranscriberLogging ? "1" : "0";
            process.StartInfo.Environment["NATASHA_FALLBACK_PER_NAMES"] = "1";
            process.StartInfo.Environment["NATASHA_STRICT_CASE"] = "0";
            var cfgDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration");
            if (Directory.Exists(cfgDir))
            {
                process.StartInfo.Environment["NATASHA_CONFIG_DIR"] = cfgDir;
            }

            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            try
            {
                Logger.Info($"Starting Python script: {scriptPath} with arguments: {arguments}");
                process.Start();
                if (!string.IsNullOrEmpty(input))
                {
                    process.StandardInput.Write(input);
                    process.StandardInput.Close();
                }

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                string output = outputBuilder.ToString();
                string error = errorBuilder.ToString();
                if (process.ExitCode != 0)
                {
                    Logger.Error($"Python script failed with exit code {process.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        Logger.Error($"Python error: {error}");
                    }

                    throw new Exception($"Python script failed: {error}");
                }

                if (!string.IsNullOrEmpty(error))
                {
                    Logger.Warn($"Python warnings: {error}");
                }

                Logger.Info($"Python script completed successfully");
                return output;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to run Python script: {ex.Message}");
                throw;
            }
            finally
            {
                process?.Dispose();
            }
        }

        public static bool IsPythonAvailable()
        {
            try
            {
                var pythonPath = GetPythonPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonPath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
