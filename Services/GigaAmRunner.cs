using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AudioRecorderTrayApp.Services
{
    public static class GigaAmRunner
    {
        private static string? TryCombineIfExists(params string[] parts)
        {
            try
            {
                var p = Path.Combine(parts);
                return File.Exists(p) ? p : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? ResolvePythonExecutable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var configured = TryCombineIfExists(baseDir, AppSettings.Current.PythonPath ?? ".venv\\Scripts\\python.exe");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
            try
            {
                var root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
                var dev = TryCombineIfExists(root, AppSettings.Current.PythonPath ?? ".venv\\Scripts\\python.exe");
                if (!string.IsNullOrWhiteSpace(dev))
                    return dev;
            }
            catch
            {
            }

            var venv = Environment.GetEnvironmentVariable("VIRTUAL_ENV");
            if (!string.IsNullOrWhiteSpace(venv))
            {
                var fromVenv = TryCombineIfExists(venv, "Scripts", "python.exe");
                if (!string.IsNullOrWhiteSpace(fromVenv))
                    return fromVenv;
            }

            var pythonEnv = Environment.GetEnvironmentVariable("PYTHON");
            if (!string.IsNullOrWhiteSpace(pythonEnv) && File.Exists(pythonEnv))
                return pythonEnv;
            var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            if (!string.IsNullOrWhiteSpace(pythonHome))
            {
                var fromHome = TryCombineIfExists(pythonHome, "python.exe");
                if (!string.IsNullOrWhiteSpace(fromHome))
                    return fromHome;
            }

            return null;
        }

        public static async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
                throw new FileNotFoundException("Audio file not found.", audioFilePath);
            Logger.Info($"[OnDemand GigaAM] Starting transcription for: {audioFilePath}");
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gigaam_server.py");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("GigaAM script not found", scriptPath);
            var sw = Stopwatch.StartNew();
            try
            {
                Logger.Info($"[OnDemand GigaAM] Using PythonService to run: {scriptPath}");
                Logger.Info($"[OnDemand GigaAM] Loading model, this may take some time...");
                var result = await PythonService.RunScriptAsync(scriptPath, $"--single \"{audioFilePath}\"");
                sw.Stop();
                if (string.IsNullOrWhiteSpace(result))
                {
                    Logger.Error("[OnDemand GigaAM] Transcription returned empty result");
                    throw new InvalidOperationException("GigaAM transcription failed: empty result");
                }

                Logger.Info($"[OnDemand GigaAM] Transcription completed in {sw.ElapsedMilliseconds}ms, result length: {result.Length}");
                return result.Trim();
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[OnDemand GigaAM] Transcription failed: {ex.Message}");
                throw new InvalidOperationException($"GigaAM transcription failed: {ex.Message}", ex);
            }
        }
    }
}
